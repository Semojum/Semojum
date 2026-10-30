using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsSettingsHelpDesignChecks
{
    internal static async Task RunAsync(Window owner, Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var defaults = new DefaultsDialog(new SettingsClient(api)) { Owner = owner }; defaults.Show();
        try
        {
            await WaitAsync(() => Descendants(defaults).OfType<ComboBox>().Any(c => c.SelectedItem is BrailleForm)); defaults.UpdateLayout();
            var groups = Descendants(defaults).OfType<TextBlock>().Select(t => t.Text).ToArray();
            check(defaults.Width == 560 && defaults.Height == 608 && new[] { "글과 규정", "줄과 빈칸", "내보낼 꼴" }.All(groups.Contains), "Default options use the three final Figma groups");
            check(Descendants(defaults).OfType<SegmentedChoice>().Count() == 6 && Descendants(defaults).OfType<ComboBox>().Count() == 3, "Default option choices and form selector remain available");
            CheckIcons(defaults, check);
            var restore = Descendants(defaults).OfType<Button>().Single(b => Equals(b.Content, "기본값 복원")); restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(fixture.PutCount == 0 && Descendants(defaults).OfType<SegmentedChoice>().First().SelectedIndex == 1, "Restoring defaults changes the dialog without saving early");
        }
        finally { defaults.Close(); }

        var save = new DefaultsDialog(new SettingsClient(api)) { Owner = owner }; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) }; var deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) =>
        {
            var form = Descendants(save).OfType<ComboBox>().FirstOrDefault(c => c.SelectedItem is BrailleForm);
            if (form is not null && save.IsEnabled && Descendants(save).OfType<SegmentedChoice>().Count() == 6)
            {
                timer.Stop(); Descendants(save).OfType<SegmentedChoice>().First().SelectedIndex = 1;
                Descendants(save).OfType<Button>().Single(b => Equals(b.Content, "확인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else if (DateTime.UtcNow >= deadline) { timer.Stop(); save.Close(); }
        };
        try { timer.Start(); var result = save.ShowDialog(); check(result == true && fixture.PutCount == 1 && fixture.Saved is { } body && body.GetProperty("hangulGrade").GetString() == "grade2" && body.GetProperty("englishGrade").GetString() == "grade1" && body.GetProperty("pageNumberLine").GetString() == "even", "Saving defaults preserves hidden English grade and page number settings"); }
        finally { timer.Stop(); }

        var about = new AboutDialog("1.2.3", () => Task.FromResult("0.0.42")) { Owner = owner }; about.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); about.UpdateLayout();
            var labels = Descendants(about).OfType<TextBlock>().Select(t => t.Text).ToArray();
            check(about.Width == 440 && about.Height == 278 && labels.Contains("V1.2.3") && about.EngineVersion.Text == "0.0.42" && labels.Contains("contact@semo-jum.com"), "About screen shows actual product, engine and contact information"); CheckIcons(about, check);
        }
        finally { about.Close(); }

        var help = new HelpWindow("점자 직접 고치기") { Owner = owner }; help.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); help.UpdateLayout();
            var tabs = Descendants(help).OfType<TabControl>().Single(); var reader = Descendants(help).OfType<FlowDocumentScrollViewer>().Single();
            var search = Descendants(help).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "도움말 검색");
            check(help.Width == 1100 && help.Height == 700 && tabs.Items.Count == 3 && new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text.Contains("점자 직접 고치기"), "Help opens the requested article in the final Figma layout"); CheckIcons(help, check);
            var tree = Descendants(help).OfType<TreeView>().Single();
            check(tree.SelectedItem is TreeViewItem { Header: "점자 직접 고치기" }, "Help highlights the currently opened article in the navigation tree");
            var selected = (TreeViewItem)tree.SelectedItem;
            check(selected.Template.FindName("TopicSurface", selected) is Border surface && Math.Abs(surface.ActualWidth - tree.ActualWidth) <= 1, "Selected help topic highlight spans the full navigation width");
            search.Text = "점자"; var searchTab = (TabItem)tabs.Items[2]; var list = ((DockPanel)searchTab.Content).Children.OfType<ListBox>().Single();
            check(tabs.SelectedIndex == 2 && list.Items.Count > 0, "Toolbar help search selects and filters the real search results");
            list.SelectedItem = list.Items.Cast<object>().First(item => !item.ToString()!.Contains("점자 직접 고치기", StringComparison.Ordinal));
            Descendants(help).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "이전").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text.Contains("점자 직접 고치기"), "Help back navigation returns to the previous article");
        }
        finally { help.Close(); }

        var early = new HelpWindow("추가 도구") { Owner = owner }; early.Show();
        try
        {
            early.UpdateLayout();
            var tree = Descendants(early).OfType<TreeView>().Single();
            var branch = tree.Items.OfType<TreeViewItem>().Single(b => Equals(b.Header, "메뉴별 기능"));
            branch.IsExpanded = true;
            var topic = branch.Items.OfType<TreeViewItem>().Single(t => Equals(t.Header, "대체 초안과 규정"));
            topic.IsSelected = true;
            // Let the deferred initial selection and WPF's item layout complete before observing the result.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); early.UpdateLayout();
            var reader = Descendants(early).OfType<FlowDocumentScrollViewer>().Single();
            var paragraphs = reader.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).Select(r => r.Text);
            check(ReferenceEquals(tree.SelectedItem, topic) && paragraphs.Contains("대체 초안과 규정"),
                "An early help-tree choice survives the deferred initial selection and shows the chosen article");
        }
        finally { early.Close(); }

        var groupChoice = new HelpWindow("추가 도구") { Owner = owner }; groupChoice.Show();
        try
        {
            groupChoice.UpdateLayout();
            var tree = Descendants(groupChoice).OfType<TreeView>().Single();
            var branch = tree.Items.OfType<TreeViewItem>().Single(b => Equals(b.Header, "문제 해결"));
            branch.IsSelected = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); groupChoice.UpdateLayout();
            check(ReferenceEquals(tree.SelectedItem, branch), "An early help group selection is not replaced by the initial article");
        }
        finally { groupChoice.Close(); }
    }
    private static void CheckIcons(Window window, Action<bool, string> check)
    {
        var icons = Descendants(window).OfType<SvgViewbox>().Where(icon => icon.IsVisible).ToArray();
        check(icons.Length > 0 && icons.All(icon => icon.Drawings is { Bounds.IsEmpty: false } && icon.ActualWidth > 0 && icon.ActualHeight > 0 && icon.ActualWidth <= icon.Width + 1 && icon.ActualHeight <= icon.Height + 1), $"{window.Title} Figma SVGs render in their intended slots");
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5); while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!condition()) throw new InvalidOperationException("Settings fixture did not load");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class Fixture : HttpMessageHandler
    {
        internal int PutCount { get; private set; }
        internal JsonElement? Saved { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object result;
            if (request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)) result = new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" };
            else if (request.RequestUri.AbsolutePath.EndsWith("/braille-forms", StringComparison.Ordinal)) result = new { items = new[] { new { formId = "builtin:GENERAL_BOOK", name = "일반 도서", builtin = true, baseFormId = (string?)null, layoutOptions = JobClient.LayoutBody(new()), brailleOptions = JobClient.OptionsBody(new()) } } };
            else if (request.Method == HttpMethod.Put)
            {
                PutCount++; using var parsed = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)); Saved = parsed.RootElement.Clone(); result = Saved.Value;
            }
            else { var values = JobClient.OptionsBody(new BrailleOptions(KoreanGrade1: true, EnglishGrade1: true)); values["formId"] = "builtin:GENERAL_BOOK"; values["pageNumberLine"] = "even"; result = values; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") };
        }
    }
}
