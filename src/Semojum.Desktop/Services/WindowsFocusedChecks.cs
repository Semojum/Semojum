using System.Diagnostics;
using System.IO;

namespace Semojum.Desktop.Services;

/// <summary>Runs existing WPF checks for one area during development; the full smoke suite remains the integration gate.</summary>
internal static class WindowsFocusedChecks
{
    private static readonly HashSet<string> Areas = new(StringComparer.OrdinalIgnoreCase)
        { "board", "institution", "editor", "appearance" };

    internal static bool IsKnownArea(string area) => Areas.Contains(area);

    internal static async Task RunAsync(string area, string directory)
    {
        if (!IsKnownArea(area)) throw new ArgumentException("Unknown check area.", nameof(area));
        Directory.CreateDirectory(directory);
        var checksPath = Path.Combine(directory, "checks.txt");
        var failurePath = Path.Combine(directory, "failure.txt");
        if (File.Exists(failurePath)) File.Delete(failurePath);
        var checks = new List<string>();
        var timer = Stopwatch.StartNew();
        void Check(bool success, string name)
        {
            if (!success) throw new InvalidOperationException(name);
            checks.Add("PASS " + name);
        }
        try
        {
            switch (area.ToLowerInvariant())
            {
                case "board":
                    await WindowsBoardDesignChecks.RunAsync(Check);
                    await WindowsBoardLatencyChecks.RunAsync(Check);
                    break;
                case "institution":
                    await WindowsInstitutionClientChecks.RunAsync(Check);
                    await WindowsInstitutionDesignChecks.RunAsync(Check);
                    await WindowsInstitutionVisualChecks.RunAsync(Check);
                    break;
                case "editor":
                    await WindowsReviewMarginChecks.RunAsync(Check);
                    await WindowsGridMarkerNavigationChecks.RunAsync(Check);
                    await WindowsEditorHighlightChecks.RunAsync(Check);
                    await WindowsSelectionNavigationChecks.RunAsync(Check, directory);
                    await WindowsSelectionLinksChecks.RunAsync(Check, directory);
                    await WindowsSourceMapChecks.RunAsync(Check);
                    break;
                case "appearance":
                    await WindowsAppearanceDefaultChecks.RunAsync(Check);
                    await WindowsCommonDesignChecks.RunAsync(directory, Check);
                    await WindowsIndividualControlStatesChecks.RunAsync(directory, Check);
                    break;
            }
        }
        finally
        {
            File.WriteAllLines(checksPath, checks);
            Console.WriteLine($"Area {area}: {checks.Count} checks passed in {timer.Elapsed.TotalSeconds:F1}s.");
        }
    }
}
