using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsQa1110SpaceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var editor = new TextEditor { Text = "가 나  다\n마지막 ", FontSize = 14, WordWrap = true };
        var window = new Window { Content = editor, Width = 300, Height = 180 }; window.Show();
        try
        {
            var original = editor.Text;
            SpaceMarkGenerator.Configure(editor, true);
            EnterMarkGenerator.Configure(editor, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
            var spaces = editor.TextArea.TextView.VisualLines.SelectMany(l => l.Elements)
                .OfType<FormattedTextElement>().Where(e => e.DocumentLength == 1).ToArray();
            check(spaces.Length == 4, "QA1110: consecutive and trailing spaces each receive a visible marker");
            var wide = new FormattedText("•", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Symbol"), 14, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
            var old = new FormattedText("·", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Symbol"), 14, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
            check(wide > old, $"QA1110: space marker is wider than the old middle dot ({wide:F2}>{old:F2})");
            editor.Select(1, 1);
            check(editor.SelectedText == " " && editor.Text == original, "QA1110: selecting a marker selects exactly one real space");
            await WindowsSmoke.RenderElementAsync(window, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qa1110-spaces.png"));
            SpaceMarkGenerator.Configure(editor, false); EnterMarkGenerator.Configure(editor, false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.TextArea.TextView.EnsureVisualLines();
            check(editor.TextArea.TextView.VisualLines.SelectMany(l => l.Elements).All(e => e is not FormattedTextElement) && editor.Text == original,
                "QA1110: hiding space and Enter markers leaves the source unchanged");
        }
        finally { window.Close(); }
    }
}
