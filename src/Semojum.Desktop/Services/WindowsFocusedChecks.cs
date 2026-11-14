using System.Diagnostics;
using System.IO;

namespace Semojum.Desktop.Services;

/// <summary>Runs existing WPF checks for one area during development; the full smoke suite remains the integration gate.</summary>
internal static class WindowsFocusedChecks
{
    private static readonly HashSet<string> Areas = new(StringComparer.OrdinalIgnoreCase)
        { "tablememo", "drafts", "qa1110", "board", "institution", "editor", "appearance", "usage", "rules", "dialogs", "windows", "account" };

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
                case "tablememo":
                    await WindowsQa1110TableChecks.RunPreviewAsync(Check);
                    await WindowsMemoDeleteChecks.RunAsync(directory, Check);
                    await WindowsMemoRestoreChecks.RunAsync(directory, Check);
                    await WindowsTableDockChecks.RunAsync(directory, Check);
                    break;
                case "drafts":
                    await WindowsDraftDockChecks.RunAsync(Check);
                    break;
                case "qa1110":
                    await WindowsQa1110Checks.RunAsync(Check);
                    break;
                case "board":
                    WindowsGridIsolationChecks.Run(Check);
                    await WindowsBoardDesignChecks.RunAsync(Check);
                    await WindowsBoardLatencyChecks.RunAsync(Check);
                    break;
                case "institution":
                    await WindowsInstitutionClientChecks.RunAsync(Check);
                    await WindowsInstitutionDesignChecks.RunAsync(Check);
                    await WindowsInstitutionVisualChecks.RunAsync(Check);
                    break;
                case "usage":
                    await WindowsUsageDesignChecks.RunAsync(Check);
                    await WindowsUsageFeedbackChecks.RunAsync(Check);
                    break;
                case "rules":
                    await WindowsRuleDesignChecks.RunAsync(Check);
                    break;
                case "dialogs":
                    await WindowsPublicInquiryFieldsChecks.RunAsync(Check);
                    await WindowsPublicDesignChecks.RunAsync(Check);
                    await WindowsFormDesignChecks.RunAsync(Check);
                    await WindowsRangeDesignChecks.RunAsync(Check);
                    await WindowsGoToDesignChecks.RunAsync(Check);
                    await WindowsNoticeDesignChecks.RunAsync(directory, Check);
                    WindowsFileDialogDesignChecks.Run(Check);
                    break;
                case "windows":
                    await WindowsOwnedModalChecks.RunAsync(Check);
                    await WindowsCaptionChecks.RunAsync(Check);
                    await WindowsEmbeddedScreenChecks.RunAsync(Check);
                    break;
                case "account":
                    await WindowsAccountChecks.RunAsync(Check);
                    WindowsLoginDesignChecks.Run(Check);
                    await WindowsLoginLegalChecks.RunAsync(Check, directory);
                    await WindowsAccountChecks.CheckLogoutCancellationAsync(Check);
                    await WindowsAccountChecks.CheckRoleChangeAsync(Check);
                    await WindowsToolbarAccountChecks.RunAsync(Check);
                    await WindowsRequiredUpdateChecks.RunAsync(Check);
                    break;
                case "editor":
                    await WindowsMathTemplateChecks.RunAsync(Check);
                    await WindowsOriginalWheelChecks.RunAsync(Check);
                    await WindowsRegionSelectionChecks.RunAsync(Check, directory);
                    await WindowsMenuDesignChecks.RunAsync(directory, Check);
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
                    await WindowsBrailleFontChecks.RunAsync(Check);
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
