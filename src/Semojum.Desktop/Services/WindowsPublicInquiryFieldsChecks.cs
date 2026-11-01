using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsPublicInquiryFieldsChecks
{
    private sealed class Fixture : HttpMessageHandler
    {
        internal readonly List<string> Bodies = [];
        internal bool Accept;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/api/public/inquiries") throw new InvalidOperationException("Unexpected fixture request");
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (Accept) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":{}}", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"UNAVAILABLE\",\"message\":\"fixture error\"}", Encoding.UTF8, "application/json") };
        }
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); var dialog = new PublicInquiryDialog(api);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        dialog.Loaded += async (_, _) =>
        {
            try
            {
                var boxes = Descendants(dialog).OfType<TextBox>().ToArray();
                var organization = boxes.FirstOrDefault(b => AutomationProperties.GetName(b) == "기관명");
                var title = boxes.FirstOrDefault(b => AutomationProperties.GetName(b) == "제목");
                check(organization is not null && title is not null, "Public inquiry accepts distinct organization and title fields");
                if (organization is null || title is null) { dialog.Close(); done.TrySetResult(); return; }
                var email = boxes.Single(b => b.MaxLength == 100);
                check(!boxes.Any(b => b.MaxLength == 50) && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "기관명") && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "제목")
                    && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("이름", StringComparison.Ordinal)), "Public inquiry shows the final address, organization, title and content fields without a sender name");
                var message = boxes.Single(b => b.AcceptsReturn);
                var send = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "보내기"));
                var consent = Descendants(dialog).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent");
                email.Text = "fixture@example.test"; organization.Text = "기관 A"; title.Text = "문의 제목"; message.Text = "본문";
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                check(fixture.Bodies.Count == 0 && dialog.IsVisible && organization.Text == "기관 A" && title.Text == "문의 제목" && message.Text == "본문"
                    && Descendants(dialog).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("동의해 주세요", StringComparison.Ordinal)),
                    "Inquiry without consent is not sent, shows an error, and preserves all fields");
                var provider = (UIElementAutomationPeer.CreatePeerForElement(consent) as CheckBoxAutomationPeer)?.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                provider?.Toggle();
                check(provider is not null && consent.IsChecked == true, "Privacy consent is accessible through the checkbox automation toggle");
                var focusGranted = consent.Focus() && consent.IsKeyboardFocused && dialog.IsActive
                    && GetForegroundWindow() == new WindowInteropHelper(dialog).Handle;
                var source = PresentationSource.FromVisual(consent);
                if (focusGranted && source is not null)
                {
                    consent.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                    consent.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                    check(consent.IsChecked == false, "The focused consent checkbox can be unchecked using the WPF Space key path");
                }
                else Console.WriteLine("SKIP: inquiry Space key requires keyboard focus on the consent checkbox.");
                if (consent.IsChecked != true) provider?.Toggle();
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (fixture.Bodies.Count < 1 && DateTime.UtcNow < deadline) await Task.Delay(25);
                await Task.Delay(100);
                if (fixture.Bodies.Count > 0)
                {
                    using var json = JsonDocument.Parse(fixture.Bodies[0]); var data = json.RootElement;
                    check(data.GetProperty("name").GetString() == "" && data.GetProperty("type").GetString() == "ONBOARDING"
                        && data.GetProperty("message").GetString() == "[계정 · 발급] 기관명: 기관 A\n제목: 문의 제목\n\n본문",
                        "The optional legacy sender name is sent empty and the category, organization and title reach the server in the message");
                }
                else check(false, "Inquiry request reaches the mock server");
                check(organization.Text == "기관 A" && title.Text == "문의 제목" && message.Text == "본문" && dialog.IsVisible,
                    "A failed inquiry retains every entered field");
                message.Text = new string('가', 2000);
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                check(fixture.Bodies.Count == 1 && message.Text.Length == 2000,
                    "Combined message over the server limit is rejected without discarding input");
                message.Text = "본문";
                var cancel = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "취소"));
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                int action = 0; var modalDeadline = DateTime.UtcNow.AddSeconds(5); var modalTimedOut = false;
                timer.Tick += (_, _) =>
                {
                    if (DateTime.UtcNow >= modalDeadline)
                    {
                        modalTimedOut = true; timer.Stop();
                        foreach (var child in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == dialog && w.IsVisible).ToArray()) child.Close();
                        return;
                    }
                    var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == dialog && n.IsVisible);
                    if (notice is null) return;
                    notice.ActionButtons[action].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                try
                {
                    timer.Start(); cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (modalTimedOut) throw new TimeoutException("Inquiry discard confirmation did not finish within 5 seconds");
                    check(dialog.IsVisible && organization.Text == "기관 A" && title.Text == "문의 제목" && message.Text == "본문" && fixture.Bodies.Count == 1,
                        "Continuing after cancel preserves the inquiry draft without sending again");
                    action = 1; modalDeadline = DateTime.UtcNow.AddSeconds(5); cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (modalTimedOut) throw new TimeoutException("Inquiry discard confirmation did not finish within 5 seconds");
                }
                finally { timer.Stop(); }
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                failure = ex; done.TrySetResult();
                foreach (var input in Descendants(dialog).OfType<TextBox>()) input.Clear();
                if (dialog.IsVisible) dialog.Close();
            }
        };
        dialog.Show(); await done.Task.WaitAsync(TimeSpan.FromSeconds(8));
        if (failure is not null) throw failure;
        var choiceOnly = new PublicInquiryDialog(api); choiceOnly.Show();
        var type = Descendants(choiceOnly).OfType<ComboBox>().Single();
        var cancelOnly = Descendants(choiceOnly).OfType<Button>().Single(b => Equals(b.Content, "취소"));
        var confirmation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) }; var choiceDeadline = DateTime.UtcNow.AddSeconds(5); var choiceTimedOut = false;
        confirmation.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= choiceDeadline)
            {
                choiceTimedOut = true; confirmation.Stop();
                foreach (var child in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == choiceOnly && w.IsVisible).ToArray()) child.Close();
                return;
            }
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == choiceOnly && n.IsVisible);
            if (notice is not null) notice.ActionButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try
        {
            type.SelectedIndex = 1;
            confirmation.Start(); cancelOnly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (choiceTimedOut) throw new TimeoutException("Inquiry type discard confirmation did not finish within 5 seconds");
            check(choiceOnly.IsVisible && type.SelectedIndex == 1 && fixture.Bodies.Count == 1,
                "Continuing after cancel preserves a changed inquiry type even when other fields are empty");
        }
        finally { confirmation.Stop(); if (choiceOnly.IsVisible) { type.SelectedIndex = 0; choiceOnly.Close(); } }
        var untouched = new PublicInquiryDialog(api); untouched.Show(); untouched.Close();
        check(!untouched.IsVisible && fixture.Bodies.Count == 1, "An untouched inquiry closes without confirmation or a request");
        var untouchedPurchase = new PublicInquiryDialog(api, "크레딧 구매 문의", PublicInquiryCategory.CreditPayment); untouchedPurchase.Show();
        await untouchedPurchase.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); untouchedPurchase.UpdateLayout();
        var initialPurchaseTitle = Descendants(untouchedPurchase).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "제목");
        var initialPurchaseCategory = Descendants(untouchedPurchase).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "분류");
        check(initialPurchaseTitle.Text == "크레딧 구매 문의" && Equals(initialPurchaseCategory.SelectedItem, "크레딧 · 결제"),
            "A purchase inquiry starts with an editable, explicit inquiry title and the 「크레딧 · 결제」 category");
        untouchedPurchase.Close();
        check(!untouchedPurchase.IsVisible && fixture.Bodies.Count == 1, "Closing an untouched purchase inquiry requires no confirmation or request");

        var purchase = new PublicInquiryDialog(api, "크레딧 구매 문의", PublicInquiryCategory.CreditPayment); purchase.Show();
        try
        {
            await purchase.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            purchase.Width = 400; purchase.UpdateLayout();
            var fields = Descendants(purchase).OfType<TextBox>().ToArray();
            var purchaseTitle = fields.Single(t => AutomationProperties.GetName(t) == "제목");
            var purchaseEmail = fields.Single(t => t.MaxLength == 100);
            var purchaseMessage = fields.Single(t => t.AcceptsReturn);
            var titleBounds = purchaseTitle.TransformToAncestor(purchase).TransformBounds(new Rect(0, 0, purchaseTitle.ActualWidth, purchaseTitle.ActualHeight));
            check(purchaseTitle.IsVisible && titleBounds.Left >= 0 && titleBounds.Right <= purchase.ActualWidth + 1,
                "Purchase inquiry title remains visible inside a narrow window");
            purchaseEmail.Text = "fixture@example.test"; purchaseMessage.Text = "크레딧 충전 방법을 알려 주세요.";
            Descendants(purchase).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent").IsChecked = true;
            Descendants(purchase).OfType<Button>().Single(b => Equals(b.Content, "보내기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var purchaseDeadline = DateTime.UtcNow.AddSeconds(5);
            while (fixture.Bodies.Count < 2 && DateTime.UtcNow < purchaseDeadline) await Task.Delay(25);
            await Task.Delay(100);
            var purchaseBodyIncludesTitle = false; string? purchaseType = null, purchaseText = null;
            if (fixture.Bodies.Count == 2)
            {
                using var sent = JsonDocument.Parse(fixture.Bodies[1]);
                purchaseType = sent.RootElement.GetProperty("type").GetString(); purchaseText = sent.RootElement.GetProperty("message").GetString();
                purchaseBodyIncludesTitle = purchaseText?.Contains("제목: 크레딧 구매 문의", StringComparison.Ordinal) == true;
            }
            check(fixture.Bodies.Count == 2 && purchase.IsVisible && purchaseMessage.Text.Contains("충전 방법", StringComparison.Ordinal)
                && purchaseBodyIncludesTitle,
                "Only an explicitly submitted purchase inquiry includes its title and retains input on server failure");
            check(purchaseType == "ETC" && purchaseText == "[크레딧 · 결제] 제목: 크레딧 구매 문의\n\n크레딧 충전 방법을 알려 주세요.",
                $"A purchase inquiry sent as opened uses type ETC and the 「[크레딧 · 결제] 」 prefix (type={purchaseType})");
            purchaseEmail.Clear(); purchaseMessage.Clear();
        }
        finally { if (purchase.IsVisible) purchase.Close(); }

        // A successful inquiry confirms receipt in the app's message window (Case 2·5 size, one 「확인」) and then closes.
        fixture.Accept = true;
        var accepted = new PublicInquiryDialog(api); string? receipt = null; string[] receiptLabels = []; var receiptTimedOut = false;
        var receiptDeadline = DateTime.UtcNow.AddSeconds(5); var receiptTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        receiptTimer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= receiptDeadline)
            {
                receiptTimedOut = true; receiptTimer.Stop();
                foreach (var child in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == accepted && w.IsVisible).ToArray()) child.Close();
                foreach (var input in Descendants(accepted).OfType<TextBox>()) input.Clear();
                if (accepted.IsVisible) accepted.Close();
                return;
            }
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == accepted && n.IsVisible);
            if (notice is null) return;
            receipt = notice.MessageText; receiptLabels = notice.ActionButtons.Select(b => (string)b.Content).ToArray(); receiptTimer.Stop();
            notice.ActionButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        accepted.Loaded += (_, _) => accepted.Dispatcher.BeginInvoke(new Action(() =>
        {
            var fields = Descendants(accepted).OfType<TextBox>().ToArray();
            fields.Single(t => t.MaxLength == 100).Text = "fixture@example.test"; fields.Single(t => t.AcceptsReturn).Text = "접수 확인";
            Descendants(accepted).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent").IsChecked = true;
            Descendants(accepted).OfType<Button>().Single(b => Equals(b.Content, "보내기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }), DispatcherPriority.ApplicationIdle);
        bool? acceptedResult;
        receiptTimer.Start(); try { acceptedResult = accepted.ShowDialog(); } finally { receiptTimer.Stop(); }
        check(!receiptTimedOut && acceptedResult == true && fixture.Bodies.Count == 3 && receipt == "문의를 접수했습니다." && receiptLabels.SequenceEqual(["확인"]),
            "A received inquiry is confirmed in the app message window, not a Windows message box, and its 「확인」 closes the inquiry");

        // Each Figma category sends the server type it maps to and prefixes its label to the message; the prefix counts toward the 2,000-character limit.
        fixture.Accept = false; var sentBefore = fixture.Bodies.Count;
        var categorized = new PublicInquiryDialog(api); categorized.Show();
        await categorized.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); categorized.UpdateLayout();
        var categoryBox = Descendants(categorized).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "분류");
        var categoryFields = Descendants(categorized).OfType<TextBox>().ToArray();
        var categoryMessage = categoryFields.Single(t => t.AcceptsReturn);
        var categorySend = Descendants(categorized).OfType<Button>().Single(b => Equals(b.Content, "보내기"));
        try
        {
            categoryFields.Single(t => t.MaxLength == 100).Text = "fixture@example.test";
            Descendants(categorized).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent").IsChecked = true;
            (string Label, string Type)[] expected = [("계정 · 발급", "ONBOARDING"), ("점역 결과", "ERROR_REPORT"), ("크레딧 · 결제", "ETC"), ("사용법", "ETC")];
            check(categoryBox.Items.Cast<string>().SequenceEqual(expected.Select(e => e.Label)) && categoryBox.SelectedIndex == 0, "The inquiry offers the four Figma categories in order, starting at the first");
            var sent = new List<string>();
            for (var i = 0; i < expected.Length; i++)
            {
                categoryBox.SelectedIndex = i; categoryMessage.Text = "분류 확인";
                categorySend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var categoryDeadline = DateTime.UtcNow.AddSeconds(5);
                while ((fixture.Bodies.Count < sentBefore + i + 1 || !categorySend.IsEnabled) && DateTime.UtcNow < categoryDeadline) await Task.Delay(25);
                if (fixture.Bodies.Count < sentBefore + i + 1) break;
                using var json = JsonDocument.Parse(fixture.Bodies[sentBefore + i]);
                sent.Add(json.RootElement.GetProperty("type").GetString() + "|" + json.RootElement.GetProperty("message").GetString());
            }
            check(sent.SequenceEqual(expected.Select(e => $"{e.Type}|[{e.Label}] 분류 확인")),
                "Each inquiry category sends its mapped server type and prefixes its label to the message (" + string.Join(", ", sent) + ")");
            categoryBox.SelectedIndex = 0; var beforeLimit = fixture.Bodies.Count; var prefix = "[계정 · 발급] ".Length;
            categoryMessage.Text = new string('가', 2000 - prefix + 1);
            categorySend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
            check(fixture.Bodies.Count == beforeLimit && categoryMessage.Text.Length == 2000 - prefix + 1
                && Descendants(categorized).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("2,000자", StringComparison.Ordinal)),
                "The category prefix counts toward the server's 2,000-character message limit");
            categoryMessage.Text = new string('가', 2000 - prefix);
            categorySend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var limitDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((fixture.Bodies.Count == beforeLimit || !categorySend.IsEnabled) && DateTime.UtcNow < limitDeadline) await Task.Delay(25);
            var sentLength = -1;
            if (fixture.Bodies.Count == beforeLimit + 1) { using var json = JsonDocument.Parse(fixture.Bodies[^1]); sentLength = json.RootElement.GetProperty("message").GetString()?.Length ?? -1; }
            check(sentLength == 2000, $"A message reaching exactly 2,000 characters with its category prefix is sent (length={sentLength})");
        }
        finally
        {
            foreach (var input in categoryFields) input.Clear();
            categoryBox.SelectedIndex = 0; if (categorized.IsVisible) categorized.Close();
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
