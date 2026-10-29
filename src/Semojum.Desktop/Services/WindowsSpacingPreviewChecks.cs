using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsSpacingPreviewChecks
{
    internal static void Run(Window owner, Action<bool, string> check)
    {
        var ctor = typeof(BraillePreviewDialog).GetConstructors().SingleOrDefault(c => c.GetParameters().Length == 7);
        check(ctor is not null, "Composition preview accepts unresolved spacing information");
        var prompts = new List<string>();
        Func<string, string, MessageBoxResult> decline = (message, _) => { prompts.Add(message); return MessageBoxResult.No; };
        string[][] pages = [["⠁", "⠃"]]; const string brf = "A\r\nB\r\n\f";
        var dialog = (BraillePreviewDialog)ctor!.Invoke([pages, brf, new PageLayout(), "조판 시험", 1, new[] { 7, 12 }, decline]);
        dialog.Owner = owner; dialog.Show();
        try
        {
            var texts = Descendants(dialog).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToArray();
            check(texts.Any(t => t.Contains("빈 줄") && t.Contains("7") && t.Contains("12")), "Composition preview identifies the blocks with unverified spacing");
            foreach (var label in new[] { "BRF 저장", "점자 TXT 저장", "점자 인쇄…" })
            {
                var button = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, label));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            check(prompts.Count == 3 && prompts.All(p => p.Contains("빈 줄") && p.Contains("7") && p.Contains("12")),
                "Saving or printing a preview asks about unresolved spacing before opening a destination");
            check(dialog.IsVisible && pages[0][0] == "⠁" && pages[0][1] == "⠃", "Declining output retains the preview and its braille cells");
        }
        finally { dialog.Close(); }
        prompts.Clear();
        var alignment = new BraillePreviewDialog(pages, brf, new PageLayout(), "정렬 시험", 1, [], [7], decline) { Owner = owner };
        alignment.Show();
        try
        {
            var texts = Descendants(alignment).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToArray();
            check(texts.Any(t => t.Contains("정렬") && t.Contains("7") && t.Contains("적용하지")), "Preview identifies unverified line alignment");
            foreach (var label in new[] { "BRF 저장", "점자 TXT 저장", "점자 인쇄…" })
                Descendants(alignment).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(prompts.Count == 3 && prompts.All(p => p.Contains("정렬") && p.Contains("7")), "Saving or printing warns about unverified alignment");
        }
        finally { alignment.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
