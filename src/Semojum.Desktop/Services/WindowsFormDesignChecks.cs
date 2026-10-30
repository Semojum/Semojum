using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsFormDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture"); var client = new SettingsClient(api);
        var forms = await client.FormsAsync();
        var form = new FormDialog(client, new(), new(), "user:1"); form.Show();
        try
        {
            await WaitAsync(() => Descendants(form).OfType<ComboBox>().Any(c => c.SelectedItem is BrailleForm { FormId: "user:1" }));
            check(form.Width == 460 && form.Height == 348, "Form dialog matches the final Figma 460x348 modal");
            check(HasIcon(form, "form-main-close.svg") && HasIcon(form, "form-main-chevron.svg"), "Form dialog uses final Figma close and selector icons");
            check(Descendants(form).OfType<ComboBox>().Any(c => c.SelectedItem is BrailleForm { FormId: "user:1" }), "An existing selected personal form is retained");
            var formPicker = Descendants(form).OfType<ComboBox>().Single(c => c.SelectedItem is BrailleForm { FormId: "user:1" });
            var summary = Descendants(form).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "FormPageRowSummary");
            check(summary.Text == "번호: 홀수 페이지 · 꼬리말 면: 홀수 페이지" && Equals(summary.ToolTip, summary.Text)
                && AutomationProperties.GetName(summary) == "페이지행 " + summary.Text,
                "Selected form reports page numbering and footer-page policies without implying footer content");
            var noPageNumber = forms[0] with { FormId = "fixture:none", Layout = forms[0].Layout with { PageNumbers = "출력 안 함", Footer = "" } };
            var longPolicy = forms[0] with { FormId = "fixture:long", Layout = forms[0].Layout with { PageNumbers = new string('가', 40), Footer = "모든 페이지" } };
            formPicker.ItemsSource = new[] { forms[0], noPageNumber, longPolicy, forms[^1] };
            formPicker.SelectedItem = noPageNumber;
            check(summary.Text == "번호: 출력 안 함 · 꼬리말 면: —", "Empty footer policy and explicit no-page-number policy are reported truthfully");
            formPicker.SelectedItem = null;
            check(summary.Text == "—" && Equals(summary.ToolTip, "—"), "No selected form does not invent page policies");
            formPicker.SelectedItem = longPolicy; form.Width = 420;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); form.UpdateLayout();
            var summaryBounds = summary.TransformToAncestor(form).TransformBounds(new Rect(0, 0, summary.ActualWidth, summary.ActualHeight));
            check(summary.IsVisible && summary.TextTrimming == TextTrimming.CharacterEllipsis && summary.ActualWidth > 0
                && summaryBounds.Left >= 0 && summaryBounds.Right <= form.ActualWidth + 1
                && Equals(summary.ToolTip, summary.Text) && AutomationProperties.GetName(summary) == "페이지행 " + summary.Text,
                "Long page policies remain within a narrow form and expose their complete text to tooltip and accessibility");
            form.Width = 460; formPicker.ItemsSource = forms; formPicker.SelectedItem = forms[^1];
            if (Environment.GetEnvironmentVariable("FORM_MAIN_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(form, capture);
        }
        finally { form.Close(); }

        var picker = new FormPickerDialog(forms, forms[0]); picker.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); picker.UpdateLayout();
            check(picker.Width == 380 && picker.Height == 208, "Five-form picker matches the final Figma 380x208 panel");
            check(HasIcon(picker, "form-picker-close.svg") && HasIcon(picker, "form-picker-edit.svg"), "Form picker uses final Figma close and edit icons");
            check(picker.ChoicesList.Items.Count == 5 && picker.Selected?.FormId == forms[0].FormId, "Form picker retains every individual form and its selected value");
            if (Environment.GetEnvironmentVariable("FORM_PICKER_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(picker, capture);
        }
        finally { picker.Close(); }

        var personal = forms.Single(f => !f.Builtin);
        var editor = new FormEditDialog(client, forms, personal, personal.Layout, personal.Options, personal.BaseFormId); editor.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.UpdateLayout();
            check(editor.Width == 560 && editor.Height == 568, "Personal form editor matches the final Figma 560x568 modal");
            check(HasIcon(editor, "form-edit-close.svg") && HasIcon(editor, "form-edit-chevron.svg"), "Personal form editor uses final Figma close and selector icons");
            check(Descendants(editor).OfType<Button>().Any(b => Equals(b.Content, "지우기") && b.IsEnabled), "A personal form can be deleted from its editor");
            check(Descendants(editor).OfType<Button>().Any(b => Equals(b.Content, "다른 이름으로 저장")) && Descendants(editor).OfType<Button>().Any(b => Equals(b.Content, "바탕 양식 값으로")), "Copying and applying base form values remain available");
            if (Environment.GetEnvironmentVariable("FORM_EDIT_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(editor, capture);
        }
        finally { editor.Close(); }

        var deletion = new DeleteFormDialog(personal.Name); deletion.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); deletion.UpdateLayout();
            check(deletion.Width == 380 && deletion.Height == 218 && HasIcon(deletion, "form-delete-close.svg"), "Deletion confirmation matches the Figma modal and close icon");
            var actionFooter = Descendants(deletion).OfType<Border>().Single(b => b.Child is StackPanel panel && panel.Children.OfType<Button>().Any(button => Equals(button.Content, "지우기")));
            check(actionFooter.BorderThickness == new Thickness(0), "Form deletion confirmation has no footer divider, matching the Figma screen");
            check(Descendants(deletion).OfType<TextBlock>().Any(t => t.Text.Contains("이미 변환한 문서", StringComparison.Ordinal)), "Deletion confirmation explains that existing documents remain");
            if (Environment.GetEnvironmentVariable("FORM_DELETE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(deletion, capture);
        }
        finally { deletion.Close(); }

        foreach (var (label, expected) in new[] { ("취소", false), ("지우기", true) })
        {
            var confirmation = new DeleteFormDialog(personal.Name);
            confirmation.Loaded += (_, _) => confirmation.Dispatcher.BeginInvoke(() =>
                Descendants(confirmation).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            check(confirmation.ShowDialog() == expected, $"Form deletion confirmation returns {expected} for {label}");
        }

        var builtInEditor = new FormEditDialog(client, forms, forms[0], forms[0].Layout, forms[0].Options, forms[0].BaseFormId); builtInEditor.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(Descendants(builtInEditor).OfType<Button>().Any(b => Equals(b.Content, "지우기") && !b.IsEnabled), "A built-in form cannot be deleted");
        }
        finally { builtInEditor.Close(); }

        var deleteEditor = new FormEditDialog(client, forms, personal, personal.Layout, personal.Options, personal.BaseFormId);
        deleteEditor.Loaded += (_, _) => deleteEditor.Dispatcher.BeginInvoke(() =>
        {
            var confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            var deadline = DateTime.UtcNow.AddSeconds(5);
            confirmTimer.Tick += (_, _) =>
            {
                var confirm = Application.Current.Windows.OfType<DeleteFormDialog>().FirstOrDefault();
                if (confirm is not null)
                {
                    confirmTimer.Stop();
                    Descendants(confirm).OfType<Button>().Single(b => Equals(b.Content, "지우기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (DateTime.UtcNow >= deadline) { confirmTimer.Stop(); deleteEditor.Close(); }
            };
            confirmTimer.Start();
            Descendants(deleteEditor).OfType<Button>().Single(b => Equals(b.Content, "지우기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        deleteEditor.ShowDialog();
        check(deleteEditor.Deleted && fixture.DeleteCount == 1 && fixture.DeletedPath is { } path && Uri.UnescapeDataString(path).EndsWith("/user:1", StringComparison.Ordinal),
            "Confirmed personal form deletion sends one request for the selected form");
    }
    private static bool HasIcon(DependencyObject root, string asset) => Descendants(root).OfType<SvgViewbox>()
        .Any(icon => icon.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true && icon.Width == 16 && icon.Height == 16 && icon.Drawings is { Bounds.IsEmpty: false });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static async Task WaitAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5); while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!predicate()) throw new InvalidOperationException("Forms fixture did not load");
    }
    private sealed class Fixture : HttpMessageHandler
    {
        internal int DeleteCount { get; private set; }
        internal string? DeletedPath { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Delete)
            {
                DeleteCount++; DeletedPath = request.RequestUri!.AbsolutePath;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":{}}", Encoding.UTF8, "application/json") });
            }
            object result = request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)
                ? new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" }
                : new { items = Enumerable.Range(0, 5).Select(i => new { formId = i == 4 ? "user:1" : "builtin:" + i, name = i == 4 ? "사용자 양식 A" : "기본 양식 " + i,
                    builtin = i != 4, baseFormId = (string?)null, layoutOptions = JobClient.LayoutBody(new()), brailleOptions = JobClient.OptionsBody(new()) }).ToArray() };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") });
        }
    }
}
