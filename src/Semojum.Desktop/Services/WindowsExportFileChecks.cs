using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsExportFileChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var folder = Path.Combine(directory, "range-output-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var window = new MainWindow(testWithoutAccount: true);
        var notices = new List<string>(); var rejectExisting = false;
        window.ExportConfirmation = (_, title) => { notices.Add(title); return rejectExisting && title == "파일 확인" ? MessageBoxResult.No : MessageBoxResult.Yes; };
        try
        {
            var document = new DocumentSession { Layout = new(Columns: 16, Lines: 4, PageNumbers: "출력 안 함", Footer: "모든 페이지"), FooterText = "바" };
            document.Add(new EditorBlock { Id = 1, Page = 1, Source = "가나다", Braille = "⠫⠉⠊", HasManualBraille = true });
            document.Add(new EditorBlock { Id = 2, Page = 2, Source = "다른 쪽", Braille = "⠙", HasManualBraille = true });
            var path = Path.Combine(folder, "fixture.smj"); await SmjFile.SaveAsync(path, SmjFile.Snapshot(document));
            Console.WriteLine("RANGE-TEST fixture saved");
            var reopened = await SmjFile.LoadAsync(path);
            typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [reopened, path, false, false]);
            Console.WriteLine("RANGE-TEST session loaded");
            var method = typeof(MainWindow).GetMethod("ExportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

            async Task Export(string proposed, int format, string chosen, bool rejectOverwrite = false)
            {
                rejectExisting = rejectOverwrite;
                window.ExportRangeSelection = range =>
                {
                    var type = typeof(ExportRangeDialog);
                    ((ComboBox)type.GetField("formatChoice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(range)!).SelectedItem = chosen;
                    ((TextBox)type.GetField("end", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(range)!).Text = "1";
                    return (bool)type.GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(range, null)!;
                };
                Console.WriteLine("RANGE-TEST exporting " + chosen);
                await ((Task)method.Invoke(window, [proposed, format, true])!).WaitAsync(TimeSpan.FromSeconds(20));
                Console.WriteLine("RANGE-TEST export returned " + chosen);
            }

            var proposedBrf = Path.Combine(folder, "source.brf");
            await Export(proposedBrf, 1, ".txt — 원문");
            var sourcePath = Path.ChangeExtension(proposedBrf, ".txt");
            check(File.Exists(sourcePath) && !File.Exists(proposedBrf) && await File.ReadAllTextAsync(sourcePath) == "가나다",
                "Changing BRF to source TXT writes matching extension and source text");

            var proposedTxt = Path.Combine(folder, "braille.txt");
            await Export(proposedTxt, 2, ".brf");
            var braillePath = Path.ChangeExtension(proposedTxt, ".brf");
            var bytes = await File.ReadAllBytesAsync(braillePath);
            check(!File.Exists(proposedTxt) && bytes.Length > 0 && bytes[^1] == 12 && bytes.All(b => b < 128)
                && DocumentExport.BrfPages(System.Text.Encoding.ASCII.GetString(bytes)).Length == 1
                && System.Text.Encoding.ASCII.GetString(bytes).Split("\r\n").Count(line => line.Trim().Length > 0) >= 2
                && notices.Contains("조판 빈 줄 확인"),
                "Changing source TXT to BRF writes the selected page with its footer and matching extension");
            await Export(proposedTxt, 2, ".brf", rejectOverwrite: true);
            check((await File.ReadAllBytesAsync(braillePath)).SequenceEqual(bytes) && !File.Exists(proposedTxt) && notices.Contains("파일 확인"),
                "Rejecting overwrite after an extension change preserves the existing BRF");
        }
        finally { window.PrepareSmokeShutdown(); window.Close(); await window.ReleaseResourcesAsync(); Directory.Delete(folder, true); }
    }
}
