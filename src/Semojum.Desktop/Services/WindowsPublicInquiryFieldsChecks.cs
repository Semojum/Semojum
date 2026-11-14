using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
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
        internal readonly List<Dictionary<string, string>> Bodies = [];
        internal readonly List<Dictionary<string, string>> FlatFields = [];
        internal bool Accept;
        internal (HttpStatusCode Status, string Code)? Reject;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/api/public/inquiries") throw new InvalidOperationException("Unexpected fixture request");
            var fields = new Dictionary<string, string>();
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                if (name is not null and not "request" and not "files") fields[name] = await part.ReadAsStringAsync(cancellationToken);
            }
            FlatFields.Add(fields);
            Bodies.Add(await FormAsync(request.Content!, cancellationToken));
            if (Reject is { } reject) return new HttpResponseMessage(reject.Status) { Content = new StringContent($"{{\"isSuccess\":false,\"code\":\"{reject.Code}\",\"message\":\"fixture rejection\"}}", Encoding.UTF8, "application/json") };
            if (Accept) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":{}}", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"UNAVAILABLE\",\"message\":\"fixture error\"}", Encoding.UTF8, "application/json") };
        }
    }
    // The app sends both ModelAttribute fields and a JSON request part for deployed RequestPart servers; each attachment
    // as a 「files」 part kept here as 「name:bytes」, joined by 「|」.
    internal static async Task<Dictionary<string, string>> FormAsync(HttpContent content, CancellationToken token)
    {
        var form = new Dictionary<string, string>();
        foreach (var part in (MultipartFormDataContent)content)
        {
            var disposition = part.Headers.ContentDisposition!; var name = disposition.Name!.Trim('"');
            if (name == "request")
            {
                if (part.Headers.ContentType?.MediaType != "application/json") throw new InvalidOperationException("The inquiry request part must be JSON");
                using var payload = System.Text.Json.JsonDocument.Parse(await part.ReadAsStringAsync(token));
                foreach (var field in payload.RootElement.EnumerateObject())
                {
                    var jsonValue = field.Value.GetString()!;
                    if (!form.TryGetValue(field.Name, out var formValue) || formValue != jsonValue)
                        throw new InvalidOperationException("Multipart field and JSON payload disagree: " + field.Name);
                }
                continue;
            }
            var value = (disposition.FileNameStar ?? disposition.FileName?.Trim('"')) is { } file ? $"{file}:{(await part.ReadAsByteArrayAsync(token)).Length}" : await part.ReadAsStringAsync(token);
            form[name] = form.TryGetValue(name, out var earlier) ? earlier + "|" + value : value;
        }
        return form;
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
                var email = boxes.Single(b => AutomationProperties.GetName(b) == "답 받을 주소");
                check(!boxes.Any(b => b.MaxLength == 50) && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "기관명") && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "제목")
                    && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("이름", StringComparison.Ordinal)), "Public inquiry shows the final address, organization, title and content fields without a sender name");
                check(email.MaxLength == 100 && organization.MaxLength == 100 && title.MaxLength == 200, "Address, organization and title accept up to the server's 100, 100 and 200 characters");
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
                    var data = fixture.Bodies[0];
                    check(fixture.FlatFields[0].GetValueOrDefault("type") == "ACCOUNT" && fixture.FlatFields[0].GetValueOrDefault("email") == "fixture@example.test"
                        && fixture.FlatFields[0].GetValueOrDefault("message") == "본문" && fixture.FlatFields[0].GetValueOrDefault("title") == "문의 제목",
                        "QA1110: inquiry supplies actual multipart form fields required by backend ModelAttribute binding");
                    check(data.GetValueOrDefault("type") == "ACCOUNT" && data.GetValueOrDefault("email") == "fixture@example.test" && data.GetValueOrDefault("title") == "문의 제목"
                        && data.GetValueOrDefault("orgName") == "기관 A" && data.GetValueOrDefault("message") == "본문" && data.GetValueOrDefault("website") == ""
                        && !data.ContainsKey("name") && !data.ContainsKey("files"),
                        "The default category goes as type ACCOUNT, and the organization and title go in their own fields instead of the message (" + string.Join(", ", data.Select(p => p.Key + "=" + p.Value)) + ")");
                }
                else check(false, "Inquiry request reaches the mock server");
                check(organization.Text == "기관 A" && title.Text == "문의 제목" && message.Text == "본문" && dialog.IsVisible,
                    "A failed inquiry retains every entered field");
                message.Text = new string('가', 2001);
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                check(fixture.Bodies.Count == 1 && message.Text.Length == 2001 && Descendants(dialog).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("2,000자", StringComparison.Ordinal)),
                    "A message over the server's 2,000 characters is rejected without discarding input");
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
            var purchaseEmail = fields.Single(t => AutomationProperties.GetName(t) == "답 받을 주소");
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
            string? purchaseType = null, purchaseText = null, purchaseSubject = null;
            if (fixture.Bodies.Count == 2)
            {
                var sent = fixture.Bodies[1];
                purchaseType = sent.GetValueOrDefault("type"); purchaseText = sent.GetValueOrDefault("message"); purchaseSubject = sent.GetValueOrDefault("title");
            }
            check(fixture.Bodies.Count == 2 && purchase.IsVisible && purchaseMessage.Text.Contains("충전 방법", StringComparison.Ordinal)
                && purchaseSubject == "크레딧 구매 문의",
                "Only an explicitly submitted purchase inquiry sends its title and retains input on server failure");
            check(purchaseType == "BILLING" && purchaseText == "크레딧 충전 방법을 알려 주세요." && !fixture.Bodies[^1].ContainsKey("orgName"),
                $"A purchase inquiry sent as opened uses type BILLING and sends the message as written (type={purchaseType})");
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
            fields.Single(t => AutomationProperties.GetName(t) == "답 받을 주소").Text = "fixture@example.test"; fields.Single(t => t.AcceptsReturn).Text = "접수 확인";
            Descendants(accepted).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent").IsChecked = true;
            Descendants(accepted).OfType<Button>().Single(b => Equals(b.Content, "보내기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }), DispatcherPriority.ApplicationIdle);
        bool? acceptedResult;
        receiptTimer.Start(); try { acceptedResult = accepted.ShowDialog(); } finally { receiptTimer.Stop(); }
        check(!receiptTimedOut && acceptedResult == true && fixture.Bodies.Count == 3 && receipt == "문의를 접수했습니다." && receiptLabels.SequenceEqual(["확인"]),
            "A received inquiry is confirmed in the app message window, not a Windows message box, and its 「확인」 closes the inquiry");

        // Each Figma category goes as its own server type with the message as written; the message alone has the 2,000-character limit.
        fixture.Accept = false; var sentBefore = fixture.Bodies.Count;
        var categorized = new PublicInquiryDialog(api); categorized.Show();
        await categorized.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); categorized.UpdateLayout();
        var categoryBox = Descendants(categorized).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "분류");
        var categoryFields = Descendants(categorized).OfType<TextBox>().ToArray();
        var categoryMessage = categoryFields.Single(t => t.AcceptsReturn);
        var categorySend = Descendants(categorized).OfType<Button>().Single(b => Equals(b.Content, "보내기"));
        try
        {
            categoryFields.Single(t => AutomationProperties.GetName(t) == "답 받을 주소").Text = "fixture@example.test";
            Descendants(categorized).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent").IsChecked = true;
            (string Label, string Type)[] expected = [("계정 · 발급", "ACCOUNT"), ("점역 결과", "RESULT"), ("크레딧 · 결제", "BILLING"), ("사용법", "USAGE")];
            check(categoryBox.Items.Cast<string>().SequenceEqual(expected.Select(e => e.Label)) && categoryBox.SelectedIndex == 0, "The inquiry offers the four Figma categories in order, starting at the first");
            var sent = new List<string>();
            for (var i = 0; i < expected.Length; i++)
            {
                categoryBox.SelectedIndex = i; categoryMessage.Text = "분류 확인";
                categorySend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var categoryDeadline = DateTime.UtcNow.AddSeconds(5);
                while ((fixture.Bodies.Count < sentBefore + i + 1 || !categorySend.IsEnabled) && DateTime.UtcNow < categoryDeadline) await Task.Delay(25);
                if (fixture.Bodies.Count < sentBefore + i + 1) break;
                sent.Add(fixture.Bodies[sentBefore + i].GetValueOrDefault("type") + "|" + fixture.Bodies[sentBefore + i].GetValueOrDefault("message"));
            }
            check(sent.SequenceEqual(expected.Select(e => $"{e.Type}|분류 확인")),
                "Each inquiry category sends its own server type and the message without a category prefix (" + string.Join(", ", sent) + ")");
            categoryBox.SelectedIndex = 0; var beforeLimit = fixture.Bodies.Count;
            categoryMessage.Text = new string('가', 2001);
            categorySend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
            check(fixture.Bodies.Count == beforeLimit && categoryMessage.Text.Length == 2001
                && Descendants(categorized).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("2,000자", StringComparison.Ordinal)),
                "A message over the server's 2,000 characters is not sent");
            categoryMessage.Text = new string('가', 2000);
            categorySend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var limitDeadline = DateTime.UtcNow.AddSeconds(5);
            while ((fixture.Bodies.Count == beforeLimit || !categorySend.IsEnabled) && DateTime.UtcNow < limitDeadline) await Task.Delay(25);
            var sentLength = fixture.Bodies.Count == beforeLimit + 1 ? fixture.Bodies[^1].GetValueOrDefault("message")?.Length ?? -1 : -1;
            check(sentLength == 2000, $"A message of exactly 2,000 characters is sent (length={sentLength})");
        }
        finally
        {
            foreach (var input in categoryFields) input.Clear();
            categoryBox.SelectedIndex = 0; if (categorized.IsVisible) categorized.Close();
        }

        // 「파일」: up to three images, documents or zip files of 10 MB each, checked before sending and sent as repeated 「files」 parts.
        var folder = Path.Combine(Path.GetTempPath(), "semojum-inquiry-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string Write(string name, int size) { var path = Path.Combine(folder, name); File.WriteAllBytes(path, new byte[size]); return path; }
        var attaching = new PublicInquiryDialog(api); attaching.Show();
        try
        {
            await attaching.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); attaching.UpdateLayout();
            var attachedList = Descendants(attaching).OfType<ListBox>().Single(l => AutomationProperties.GetAutomationId(l) == "PublicInquiryAttachments");
            string Rejected(params string[] paths) { try { attaching.Attach(paths); return ""; } catch (ArgumentException ex) { return ex.Message; } }
            var wrongType = Rejected(Write("setup.exe", 10)); var tooBig = Rejected(Write("scan.pdf", 10 * 1024 * 1024 + 1));
            check(wrongType.Contains("이미지·문서·zip", StringComparison.Ordinal) && tooBig.Contains("10MB", StringComparison.Ordinal) && attachedList.Items.Count == 0,
                $"An executable or a file over 10 MB is refused before it is attached ({wrongType} / {tooBig})");
            var first = Write("작업정보.txt", 5); var second = Write("page.png", 7); var third = Write("result.zip", 9);
            var fourth = Rejected(first, second, third, Write("extra.pdf", 3));
            attaching.Attach([first, second, third]); attaching.UpdateLayout();
            check(fourth.Contains("최대 3개", StringComparison.Ordinal) && attachedList.Items.Count == 3 && attachedList.IsVisible && Rejected(Write("more.pdf", 3)).Contains("최대 3개", StringComparison.Ordinal),
                "Four files at once, or a fourth file later, are refused while three are attached and listed");
            Descendants(attaching).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "답 받을 주소").Text = "fixture@example.test";
            var attachingMessage = Descendants(attaching).OfType<TextBox>().Single(t => t.AcceptsReturn); attachingMessage.Text = "첨부 확인";
            Descendants(attaching).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent").IsChecked = true;
            var attachingSend = Descendants(attaching).OfType<Button>().Single(b => Equals(b.Content, "보내기"));
            async Task<Dictionary<string, string>?> SendAsync()
            {
                var before = fixture.Bodies.Count; attachingSend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while ((fixture.Bodies.Count == before || !attachingSend.IsEnabled) && DateTime.UtcNow < deadline) await Task.Delay(25);
                attaching.UpdateLayout(); return fixture.Bodies.Count > before ? fixture.Bodies[^1] : null;
            }
            var withFiles = await SendAsync();
            check(withFiles?.GetValueOrDefault("files") == "작업정보.txt:5|page.png:7|result.zip:9" && withFiles.GetValueOrDefault("message") == "첨부 확인" && withFiles.GetValueOrDefault("type") == "ACCOUNT",
                "Attached files go with the inquiry as repeated 「files」 parts beside the fields (" + withFiles?.GetValueOrDefault("files") + ")");
            fixture.Reject = (HttpStatusCode.BadRequest, "INQUIRY4001"); await SendAsync();
            var countError = Descendants(attaching).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "첨부는 이미지·문서·zip 파일로 최대 3개까지 보낼 수 있습니다.");
            fixture.Reject = (HttpStatusCode.RequestEntityTooLarge, "INQUIRY4130"); await SendAsync();
            var sizeError = Descendants(attaching).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "첨부 파일은 한 개에 10MB까지 보낼 수 있습니다.");
            check(countError && sizeError && attaching.IsVisible && attachedList.Items.Count == 3 && attachingMessage.Text == "첨부 확인",
                "INQUIRY4001 and INQUIRY4130 explain the attachment limits and keep the draft and its files");
            fixture.Reject = null;
            attachedList.SelectedIndex = 1; ((MenuItem)attachedList.ContextMenu!.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(attachedList.Items.Cast<object>().Select(a => a.ToString()).SequenceEqual(["작업정보.txt", "result.zip"]), "A chosen attachment can be removed before sending");
            // Attached files alone are a draft worth confirming before the window closes.
            attachingMessage.Clear(); Descendants(attaching).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "답 받을 주소").Clear();
            var asked = false; var keep = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) }; var keepDeadline = DateTime.UtcNow.AddSeconds(5);
            keep.Tick += (_, _) =>
            {
                var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == attaching && n.IsVisible);
                if (notice is not null) { asked = true; keep.Stop(); notice.ActionButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else if (DateTime.UtcNow >= keepDeadline) keep.Stop();
            };
            try { keep.Start(); attaching.Close(); } finally { keep.Stop(); }
            check(asked && attaching.IsVisible && attachedList.Items.Count == 2, "Closing with only attachments asks first, and 「계속 작성」 keeps them");
        }
        finally
        {
            foreach (var input in Descendants(attaching).OfType<TextBox>()) input.Clear();
            var list = Descendants(attaching).OfType<ListBox>().SingleOrDefault(l => AutomationProperties.GetAutomationId(l) == "PublicInquiryAttachments");
            for (var left = 3; left > 0 && list?.Items.Count > 0; left--) { list.SelectedIndex = 0; ((MenuItem)list.ContextMenu!.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
            fixture.Reject = null; if (attaching.IsVisible) attaching.Close();
            try { Directory.Delete(folder, true); } catch (IOException) { }
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
