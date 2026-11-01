using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsExportFileChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var folder = Path.Combine(directory, "range-output-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var window = new MainWindow(testWithoutAccount: true);
        var notices = new List<(string Message, string[] Labels, bool Red)>(); var rejectExisting = false; var clicked = -2;
        window.ExportConfirmation = notice =>
        {
            notices.Add((notice.MessageText, notice.ActionButtons.Select(b => (string)b.Content).ToArray(),
                notice.ActionButtons[0].Background is SolidColorBrush { Color: var c } && c == Color.FromRgb(196, 43, 28)));
            if (!rejectExisting || notice.MessageText != "형식 변경으로 선택한 파일이 이미 있습니다.") { notice.Close(); return 0; }
            // Show the real message window once and press 「취소」 so the button, not only the hook, decides.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); notice.Close(); };
            notice.Loaded += (_, _) => notice.Dispatcher.BeginInvoke(new Action(() => notice.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            timer.Start(); notice.ShowDialog(); timer.Stop(); clicked = notice.Choice; return notice.Choice;
        };
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
                && !notices.Any(n => n.Message == "조판 빈 줄을 확인해 주세요."),
                "Changing source TXT to BRF writes the selected page with its footer and matching extension; edited braille without blank lines is not questioned");
            await Export(proposedTxt, 2, ".brf", rejectOverwrite: true);
            check((await File.ReadAllBytesAsync(braillePath)).SequenceEqual(bytes) && !File.Exists(proposedTxt) && clicked == 1
                && notices.Any(n => n.Message == "형식 변경으로 선택한 파일이 이미 있습니다." && n.Labels.SequenceEqual(["덮어쓰기", "취소"]) && n.Red),
                "Pressing 「취소」 in the red overwrite notice after an extension change preserves the existing BRF");

            // A-5: the engine lays out page blank lines (translate_page). Only an edited block whose typed blank line meets one is questioned.
            var first = reopened.Blocks[0]; first.Metadata = new(Type: "title", HeadingLevel: 2); first.Braille = "⠫⠉⠊\n\n";
            notices.Clear(); await Export(proposedTxt, 2, ".brf");
            check(notices.Count(n => n.Message == "조판 빈 줄을 확인해 주세요.") == 1
                && notices.Single(n => n.Message == "조판 빈 줄을 확인해 주세요.").Labels.SequenceEqual(["내보내기", "취소"]) && !notices.Single(n => n.Message == "조판 빈 줄을 확인해 주세요.").Red
                && first.Braille == "⠫⠉⠊\n\n" && first.HasManualBraille,
                "A manually edited title whose typed blank line meets the layout blank line asks once before export and keeps its cells");
            first.Braille = "⠫⠉⠊"; first.Translation = BrailleTranslation.Plain("⠫⠉⠊") with { SourceText = first.Source }; first.HasManualBraille = false;
            notices.Clear(); await Export(proposedTxt, 2, ".brf");
            check(!notices.Any(n => n.Message == "조판 빈 줄을 확인해 주세요.") && File.Exists(braillePath),
                "An engine-translated block never raises the blank-line notice on export");
        }
        finally { window.PrepareSmokeShutdown(); window.Close(); await window.ReleaseResourcesAsync(); Directory.Delete(folder, true); }
    }
}
