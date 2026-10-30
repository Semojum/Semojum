using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsQa4Checks
{
    internal static async Task RunAsync(MainWindow owner, string directory, Action<bool, string> check, Func<Window, string, Task> render)
    {
        void Stage(string name) => File.AppendAllText(Path.Combine(directory, "qa-progress.txt"), name + Environment.NewLine);
        await WindowsSearchSafetyChecks.RunAsync(check);
        await WindowsDraftSafetyChecks.RunAsync(check, directory);
        await WindowsNotificationChecks.RunAsync(check);
        await WindowsPublicInquiryFieldsChecks.RunAsync(check);
        await WindowsPublicDesignChecks.RunAsync(check);
        await WindowsBoardDesignChecks.RunAsync(check);
        await WindowsUsageDesignChecks.RunAsync(check);
        await WindowsUsageFeedbackChecks.RunAsync(check);
        await WindowsCommonDesignChecks.RunAsync(directory, check);
        await WindowsIndividualControlStatesChecks.RunAsync(directory, check);
        await WindowsOwnedModalChecks.RunAsync(check);
        await WindowsReviewMarginChecks.RunAsync(check);
        await WindowsManualReviewChecks.RunAsync(check, directory);
        WindowsReviewPerformanceChecks.Run(check);
        await WindowsReviewProjectionChecks.RunAsync(check);
        await WindowsGridMarkerNavigationChecks.RunAsync(check);
        await WindowsConversionOutcomeChecks.RunAsync(check);
        await WindowsUpdateDocumentSafetyChecks.RunAsync(check);
        await WindowsAppearanceDefaultChecks.RunAsync(check);
        await WindowsRuleDesignChecks.RunAsync(check);
        await WindowsSettingsHelpDesignChecks.RunAsync(owner, check);
        await WindowsHelpFontChecks.RunAsync(directory, check);
        await WindowsMenuDesignChecks.RunAsync(directory, check);
        WindowsEditorDialogDesignChecks.Run(owner, check);
        await WindowsPanelDesignChecks.RunAsync(directory, check);
        await WindowsStatusZoomChecks.RunAsync(directory, check);
        await WindowsToolbarAccountChecks.RunAsync(check);
        await WindowsFormDesignChecks.RunAsync(check);
        WindowsFooterFormulaDesignChecks.Run(owner, check);
        WindowsVisualDescriptionDesignChecks.Run(owner, check);
        WindowsLineTranslationDesignChecks.Run(owner, check);
        await WindowsEditorHighlightChecks.RunAsync(check);
        await WindowsCreditRecoveryChecks.RunAsync(check, directory);
        await WindowsUncertainRestartChecks.RunAsync(check, directory);
        WindowsSixDotChecks.Run(owner, check, Stage);
        WindowsImageOrderChecks.Run(owner, check);
        await WindowsFileOpenChecks.RunAsync(directory, check);
        await WindowsFileEdgeChecks.RunAsync(directory, check);
        await WindowsRangeDesignChecks.RunAsync(check);
        WindowsFileDialogDesignChecks.Run(check);
        WindowsCaseDesignChecks.Run(check);
        await WindowsNoticeDesignChecks.RunAsync(directory, check);
        await WindowsDraftChecks.RunAsync(owner, check);
        await WindowsDraftApplyChecks.RunAsync(directory, check);
        await WindowsDraftDockChecks.RunAsync(check);
        await WindowsDraftPendingChecks.RunAsync(check);
        await WindowsTableDraftChecks.RunAsync(directory, check);
        await WindowsTableDockChecks.RunAsync(directory, check);
        await WindowsMathSafetyChecks.RunAsync(check, directory);
        await WindowsMathDockChecks.RunAsync(check, directory);
        await WindowsInlineMathChecks.RunAsync(check, directory);
        await WindowsSelectionNavigationChecks.RunAsync(check, directory);
        await WindowsSelectionLinksChecks.RunAsync(check, directory);
        await WindowsSourceMapChecks.RunAsync(check);
        await WindowsTabPanelChecks.RunAsync(directory, check);
        await WindowsTabProgressChecks.RunAsync(check);
        await WindowsGoToDesignChecks.RunAsync(check);
        await WindowsUploadLimitChecks.RunAsync(directory, check);
        await WindowsUploadSafetyChecks.RunAsync(directory, check);
        await WindowsExportFileChecks.RunAsync(directory, check);
        WindowsExportRangeChecks.Run(owner, check);
        WindowsSpacingPreviewChecks.Run(owner, check);
        await WindowsResponsiveChecks.RunAsync(check);
        await WindowsCaptionChecks.RunAsync(check);
        await WindowsLongDocumentChecks.RunAsync(directory, check);
        await WindowsMemoDeleteChecks.RunAsync(directory, check);
        await WindowsMemoRestoreChecks.RunAsync(directory, check);
        await WindowsMemoHoverChecks.RunAsync(check);
        await WindowsBoardLatencyChecks.RunAsync(check);
        await WindowsUpdateSafetyChecks.RunAsync(check);
        await WindowsRequiredUpdateChecks.RunAsync(check);
        await WindowsAccountChecks.RunAsync(check);
        await WindowsLogoutDocumentSafetyChecks.RunAsync(check);
        await WindowsInstitutionClientChecks.RunAsync(check);
        await WindowsInstitutionDesignChecks.RunAsync(check);
        await WindowsInstitutionVisualChecks.RunAsync(check);
        Stage("login design start");
        WindowsLoginDesignChecks.Run(check);
        Stage("login design complete; network message start");
        WindowsAccountChecks.CheckLoginNetworkMessage(owner, check);
        Stage("network message complete; institution login start");
        WindowsAccountChecks.CheckInstitutionLoginRouting(owner, check);
        Stage("institution login complete; logout cancellation start");
        await WindowsAccountChecks.CheckLogoutCancellationAsync(check);
        Stage("logout cancellation complete; role change start");
        await WindowsAccountChecks.CheckRoleChangeAsync(check);
        Stage("role change complete; view commands start");
        await CheckViewCommandsAsync(owner, check);
        Stage("view commands complete; capture checks start");
        CheckBrailleSurface(owner, check, Stage);
        var faceElement = Children(owner.Braille.GridView).OfType<FrameworkElement>().First(e => e.GetType().Name == "Face");
        var face = (BrailleGridFace)faceElement.GetType().GetProperty("Data")!.GetValue(faceElement)!;
        var firstCell = face.Rows.SelectMany((row, y) => row.Select((cell, x) => (cell, x, y))).First(c => c.cell is not null);
        var hit = (GridCell?)faceElement.GetType().GetMethod("Hit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(faceElement, [new Point(52 + firstCell.x * 16 + 4, firstCell.y * 28 + 4)]);
        check(hit?.Block == firstCell.cell!.Block && hit.Offset == firstCell.cell.Offset,
            "Visual parity braille pointer geometry maps the 52px gutter and 16x28 cells to the original document offsets");
        owner.ZoomPicker.SelectedItem = "125%";
        check(owner.SourceEditor.FontSize == 17.5 && owner.Braille.GridView.FontSize == 27.5,
            "Editor zoom resizes the visible braille grid and source text together");
        owner.ZoomPicker.SelectedItem = "100%";
        owner.UpdateLayout();
        File.WriteAllText(Path.Combine(directory, "visual-geometry.json"), JsonSerializer.Serialize(new
        {
            SourcePadding = owner.PageEditor.Editor.Padding.ToString(),
            SourceMargins = owner.PageEditor.Editor.TextArea.LeftMargins.OfType<FrameworkElement>().Select(m => new { Type = m.GetType().Name, Width = m.ActualWidth, Margin = m.Margin.ToString() }),
            SourceTextX = owner.PageEditor.Editor.TextArea.TextView.TransformToAncestor(owner.PageEditor).Transform(new Point()).X,
            SourceBaseline = owner.PageEditor.Editor.FontFamily.Baseline,
            SourceGlyphFonts = GlyphFonts(owner.PageEditor.Editor.TextArea.TextView).Distinct().ToArray(),
            BrailleViewport = Children(owner.Braille.GridView).OfType<ScrollViewer>().Select(s => new { s.ViewportWidth, s.ExtentWidth, s.ComputedHorizontalScrollBarVisibility }),
            InterfaceFont = owner.FontFamily.Source,
            Dpi = VisualTreeHelper.GetDpi(owner).PixelsPerInchX,
            FontResources = Fonts.GetFontFamilies(Controls.BundledFonts.DirectoryUri).Select(f => f.Source)
        }, new JsonSerializerOptions { WriteIndented = true }));
        var renderedFonts = GlyphFonts(owner.PageEditor.Editor.TextArea.TextView).ToArray();
        check(renderedFonts.Any(f => f.EndsWith("Inter-Regular.ttf", StringComparison.OrdinalIgnoreCase)) && renderedFonts.Any(f => f.EndsWith("NotoSansKR-Regular.ttf", StringComparison.OrdinalIgnoreCase)) && !renderedFonts.Any(f => f.Contains("/WINDOWS/FONTS/", StringComparison.OrdinalIgnoreCase)),
            "Source editor renders with bundled Inter and Noto Sans KR instead of silently substituting Windows fonts");
        owner.Session.ErrorNotice = "작업 수신 오류 · 원문은 유지됩니다.";
        await render(owner, Path.Combine(directory, "43-operation-error.png"));
        check(owner.OperationErrorBanner.Visibility == Visibility.Visible, "QA4 asynchronous failures remain visible outside the options-only status bar");
        owner.Session.ErrorNotice = "";
        owner.UpdateLayout();
        check(owner.OperationErrorBanner.Visibility == Visibility.Collapsed, "QA4 operation notice clears without changing document contents");
        // The native IME itself requires physical keyboard testing. This checks the
        // event ordering that previously let WPF retire its context before completion.
        var imeEditor = new ICSharpCode.AvalonEdit.TextEditor { Text = "기존", Height = 80 };
        var focusTarget = new TextBox(); var imePanel = new StackPanel();
        imePanel.Children.Add(imeEditor); imePanel.Children.Add(focusTarget);
        var pending = true;
        NativeIme.Attach(imeEditor, () =>
        {
            if (!pending) return;
            pending = false; imeEditor.TextArea.PerformTextInput("가");
        });
        var imeWindow = new Window { Owner = owner, Content = imePanel, Width = 360, Height = 180 };
        imeWindow.Show();
        try
        {
            imeEditor.Focus(); imeEditor.CaretOffset = imeEditor.Document.TextLength;
            focusTarget.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            check(imeEditor.Text == "기존가" && imeEditor.TextArea.IsKeyboardFocused,
                "IME boundary completes pending text on another control's click before focus changes");
            focusTarget.Focus();
            check(imeEditor.Text == "기존가" && focusTarget.Text == "" && focusTarget.IsKeyboardFocused,
                "IME boundary keeps completed text in its original editor when focus moves");
            imeWindow.Activate(); imeEditor.Focus(); imeEditor.CaretOffset = imeEditor.Document.TextLength; pending = true;
            owner.Activate();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(imeEditor.Text == "기존가가" && !imeWindow.IsActive,
                "IME boundary completes pending text before native window deactivation");
        }
        finally { imeWindow.Close(); }
        using var handler = new Fixture(); using var http = new HttpClient(handler); var api = new SemojumApiClient(http);
        await api.LoginAsync("qa-ui", "fixture-only"); var settings = new SettingsClient(api);
        var forms = await settings.FormsAsync(); var defaults = await settings.DefaultsAsync();
        var general = forms[0] with { FormId = "builtin:GENERAL_BOOK", Name = "일반 도서", Builtin = true };
        var custom = forms[1];
        var defaultFormOpen = new OpenDocumentDialog([forms[0], general, custom], defaults with { FormId = null });
        check(defaultFormOpen.FormId == "builtin:GENERAL_BOOK", "Upload defaults to general book when no account form is saved");
        var customFormOpen = new OpenDocumentDialog([forms[0], general, custom], defaults with { FormId = custom.FormId });
        check(customFormOpen.FormId == custom.FormId && customFormOpen.Forms.Items.Cast<BrailleForm>().Any(f => f.FormId == custom.FormId),
            "Upload retains the saved custom form and keeps individually named custom forms selectable");
        async Task Capture(Window window, string file, Action? assertions = null)
        {
            window.Owner = owner; window.Show();
            try { await render(window, Path.Combine(directory, file)); assertions?.Invoke(); }
            finally { window.Close(); }
        }
        bool? Modal(Window window, Action interaction)
        {
            Exception? failure = null;
            window.Owner = owner;
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { interaction(); }
                catch (Exception ex) { failure = ex; }
                finally { if (window.IsVisible) window.Close(); }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var result = window.ShowDialog();
            if (failure is not null) throw failure;
            return result;
        }
        void Key(UIElement target, Window window, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        var open = new OpenDocumentDialog(forms, defaults);
        open.SetPaths(["one.png", "two.jpg"]);
        check(open.Paths.Length == 2 && open.FormId == "builtin:EXAM" && open.Settings.Options.EnglishGrade1, "QA4 unified open preserves account options and ordered image selection");
        try { open.SetPaths(["one.pdf", "two.txt"]); throw new InvalidOperationException("Mixed documents accepted"); } catch (ArgumentException) { }
        check(open.Paths.SequenceEqual(new[] { "one.png", "two.jpg" }), "QA4 invalid file selection leaves previous valid selection intact");
        open.SetPaths(["saved.smj"]); check(!open.Forms.IsEnabled, "QA4 SMJ open retains the saved document settings");
        open.SetPaths(["new.txt"]); check(open.Forms.IsEnabled, "QA4 choosing TXT re-enables conversion options");
        await Capture(open, "30-unified-open.png");
        var offlineOpen = new OpenDocumentDialog([], defaults, false); offlineOpen.SetPaths(["saved.smj"]);
        await Capture(offlineOpen, "42-offline-open.png", () => check(Children(offlineOpen).OfType<Button>().Any(b => b.Content?.ToString() == "열기" && b.IsEnabled), "QA4 local SMJ remains openable when server settings are unavailable"));
        var options = new OptionsEditor(); options.Set(new(EnglishGrade1: true));
        check(options.Value.EnglishGrade1 && !Children(options).OfType<TextBlock>().Any(t => t.Text == "영문"), "QA4 hidden English grade remains preserved when other defaults are edited");
        await Capture(new DefaultsDialog(settings), "31-defaults.png", () =>
            check(handler.Paths.Contains("/api/users/braille-options") && handler.Paths.Contains("/api/users/braille-forms"), "QA4 defaults loads stored settings and real form values through the API contract"));
        await Capture(new FormNameDialog(), "32-form-name.png");
        await Capture(new FormEditDialog(settings, forms, forms[1], forms[1].Layout, forms[1].Options, forms[0].FormId), "33-form-edit.png");
        await Capture(new FormDialog(settings, new(), new(), "builtin:TEXTBOOK"), "34-forms.png");
        var formChoices = new[] { "초등 교과서", "중·고 교과서", "자습서", "시험지", "일반 도서", "사용자 양식" }
            .Select((name, index) => forms[0] with { FormId = "fixture:" + index, Name = name }).ToArray();
        var picker = new FormPickerDialog(formChoices, formChoices[1]);
        await Capture(picker, "57-form-picker.png", () => check(picker.ChoicesList.Items.Count == 6 && Children(picker).OfType<ScrollViewer>().All(s => s.ComputedVerticalScrollBarVisibility == Visibility.Collapsed),
            "Figma 3966 form picker lists six forms in 24px rows without a scrollbar"));
        var chosenForm = new FormPickerDialog(formChoices, formChoices[1]);
        check(Modal(chosenForm, () => { chosenForm.ChoicesList.SelectedIndex = 3; Key(chosenForm.ChoicesList, chosenForm, System.Windows.Input.Key.Enter); }) == true && chosenForm.Selected == formChoices[3],
            "Form picker accepts the keyboard-selected actual form on Enter");
        var cancelledForm = new FormPickerDialog(formChoices, formChoices[1]);
        check(Modal(cancelledForm, () => { cancelledForm.ChoicesList.SelectedIndex = 3; Key(cancelledForm, cancelledForm, System.Windows.Input.Key.Escape); }) != true,
            "Form picker Escape cancels a changed choice without applying it to the caller");
        owner.Welcome.Visibility = Visibility.Visible;
        await WindowsSmoke.CaptureComponentAsync(owner.WelcomeCard, owner, 420, 260, Path.Combine(directory, "58-empty-state.png"));
        owner.Welcome.Visibility = Visibility.Collapsed;
        var goTo = new GoToDialog(SearchPanel.Text, (_, _) => Task.FromResult(""));
        await Capture(goTo, "60-go-to-no-previous.png", () =>
        {
            var buttons = Children(goTo).OfType<Button>().Select(button => button.Content?.ToString()).ToArray();
            check(buttons.Contains("이동") && buttons.Contains("닫기") && !buttons.Contains("이전"),
                "Go-to dialog retains navigation and close without a previous button");
        });
        var contextMenu = owner.PageEditor.Editor.ContextMenu; contextMenu.PlacementTarget = owner.PageEditor.Editor; contextMenu.IsOpen = true;
        try
        {
            await WindowsSmoke.RenderElementAsync(contextMenu, Path.Combine(directory, "59-editor-context.png"));
            var labels = contextMenu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
            check(labels.SequenceEqual(new[] { "메모 추가", "잘라내기", "복사", "붙여넣기", "대체 초안 보기", "적용 규정 보기", "정렬", "수식 입력창" }),
                "Text context menu retains memo, draft and rule actions without emphasis or page break");
            check(!owner.Braille.GridView.ContextMenu.Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == "쪽바꿈"),
                "Braille context menu does not offer page break");
        }
        finally { contextMenu.IsOpen = false; }
        if (Environment.GetEnvironmentVariable("SEMOJUM_FONT_PROBE") == "1")
        {
            var fontDirectory = Path.Combine(directory, "font-probe"); Directory.CreateDirectory(fontDirectory);
            var originalFont = contextMenu.FontFamily;
            var families = new[] { "Malgun Gothic", "Noto Sans KR", "Noto Sans CJK KR", "Noto Sans SC", "NanumGothic", "Gulim" };
            for (var fontIndex = 0; fontIndex < families.Length; fontIndex++)
            foreach (var grayscale in new[] { false, true })
            {
                contextMenu.FontFamily = new FontFamily(families[fontIndex]);
                TextOptions.SetTextRenderingMode(contextMenu, grayscale ? TextRenderingMode.Grayscale : TextRenderingMode.Auto);
                contextMenu.IsOpen = true;
                try { await WindowsSmoke.RenderElementAsync(contextMenu, Path.Combine(fontDirectory, $"{fontIndex}-{(grayscale ? "gray" : "auto")}.png")); }
                finally { contextMenu.IsOpen = false; }
            }
            contextMenu.FontFamily = originalFont; TextOptions.SetTextRenderingMode(contextMenu, TextRenderingMode.Auto);
            File.WriteAllText(Path.Combine(fontDirectory, "fonts.json"), JsonSerializer.Serialize(families));
        }
        owner.Session.CreditNotice = "크레딧이 소진되었습니다. (47/128쪽 완료)";
        owner.PageEditor.Editor.Focus();
        await render(owner, Path.Combine(directory, "66-credit-state-full.png"));
        owner.Session.CreditNotice = "";
        var menuIndex = 72;
        foreach (var menuItem in owner.ApplicationMenu.Items.OfType<MenuItem>())
        {
            menuItem.IsSubmenuOpen = true;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            try
            {
                if (menuItem.Template.FindName("PART_Popup", menuItem) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement dropdown })
                    await WindowsSmoke.RenderElementAsync(dropdown, Path.Combine(directory, $"{menuIndex++}-main-menu.png"));
            }
            finally { menuItem.IsSubmenuOpen = false; }
        }
        await Capture(new VisualDescriptionDialog(VisualDescriptions.Defaults()), "35-visual-types.png", () =>
            check(VisualDescriptions.Defaults().Count == 5, "QA4 visual types retain all five supported type policies"));
        var rules = new RuleDialog(api, [], "", "", "음악");
        await Capture(rules, "36-rules.png", () =>
        {
            var rows = rules.RuleResults.Children.OfType<Expander>().ToArray();
            check(rows.Length == 2, "QA4 rule search renders both searchable and translatable rules");
            rows[0].IsExpanded = true; rows[1].IsExpanded = true;
            check(!rows[0].IsExpanded && rows[1].IsExpanded, "QA4 rule accordion keeps one rule expanded");
            check(Children((DependencyObject)rows[0].Content).OfType<TextBlock>().Any(t => t.Text == "첫 줄\n  ⠁⠃ 원문\n마지막 줄"), "QA4 expanded rule retains full text, line breaks and indentation");
        });
        var usage = new UsageDialog(api);
        await Capture(usage, "37-usage.png", () =>
        {
            check(usage.Navigation.Items.Count == 5 && usage.Navigation.TabStripPlacement == Dock.Left, "QA4 my page has five vertical sections");
            check(Children(usage).OfType<TextBlock>().Any(t => t.Text == "8,251") && Children(usage).OfType<TextBlock>().Any(t => t.Text.Contains("할당") && t.Text.Contains("10,000") && t.Text.Contains("크레딧")), "QA4 usage uses the returned credit balance and allocation");
            check(Children(usage).OfType<ProgressBar>().Any(b => b.Value == 17.49), "QA4 usage percentage populates the progress bar");
            var displayedUsage = Children(usage).OfType<ProgressBar>().First(b => b.Value == 17.49);
            check(displayedUsage.Template.FindName("RangeFill", displayedUsage) is FrameworkElement fill && fill.ActualWidth > 80,
                "Usage progress remains visibly filled after its asynchronous value loads");
            check(handler.Paths.All(p => !p.StartsWith("/api/org/")), "QA4 normal account never requests privileged institution billing endpoints");
        });
        check(UsageDialog.StatusLabel("COMPLETED", false) == "완료" && UsageDialog.StatusLabel("COMPLETED", true) == "일부 쪽 실패" && UsageDialog.StatusLabel("FAILED", true) == "변환 실패", "QA4 job statuses use user-facing labels");
        var footer = new FooterDialog(owner.Session);
        await Capture(footer, "38-footers.png", () => check(Children(footer).OfType<DataGrid>().Single().Columns.Count == 4, "Footer manager includes start, alignment, content and deletion columns"));
        var footerFixture = new DocumentSession { FooterRanges = [new(1, 12, "Ⅰ 01 사회·문화 현상", "center"), new(13, 28, "Ⅰ 02 사회·문화 현상의 탐구", "center"), new(29, 44, "Ⅱ 01 개인과 사회 구조", "center"), new(45, 60, "Ⅱ 02 일탈 행동", "right")] };
        await Capture(new FooterDialog(footerFixture), "44-footers-populated.png");
        await Capture(new FooterInputDialog(), "45-footer-input.png");
        var memoInput = new MemoInputDialog("김점역"); memoInput.Input.Text = "원문이 「작다」인데 그래프를 보면";
        await Capture(memoInput, "46-memo-input-populated.png", () => check(Children(memoInput).OfType<SharpVectors.Converters.SvgViewbox>().All(i => i.Drawings is { Bounds.IsEmpty: false }), "Memo author avatar renders the downloaded Figma SVG resource"));
        var board = new BoardClient(api);
        await Capture(new BoardDialog(board, "로컬 화면 검증용"), "47-board-list.png");
        await Capture(new PostEditorDialog(board, null, "로컬 화면 검증용"), "48-board-write.png");
        var samplePost = new BoardPost(126, "크레딧 추가 요청드립니다", "이번 달 분량이 모자랍니다. 300쪽 추가 부탁드립니다.\n세금계산서는 기관 명의로 발행해 주시면 됩니다.", "test***", true, true, false, "2026. 10. 06. 14:21", "", 4, [new(1, "사업자등록증.pdf", 204800, "https://example.invalid/qa-attachment.pdf")], [new(1, "300쪽 반영했습니다. 마이페이지에서 남은 쪽 확인해 주세요.\n세금계산서는 담당자가 따로 연락드립니다.", false, "2026. 10. 06. 17:40", "")]);
        await Capture(new PostDialog(board, samplePost, "로컬 화면 검증용"), "49-board-post.png");
        await Capture(new NotificationsDialog(board, "로컬 화면 검증용"), "50-notifications.png");
        await Capture(new SaveAsDialog("2026_수능특강_사회문화.smj", null), "53-save-as.png");
        await Capture(new DeleteFormDialog("중학 수학 (28줄)"), "54-form-delete.png");
        await Capture(FileNotices.UnsupportedFormat(), "60-unsupported-file.png");
        await Capture(FileNotices.OversizedFile(), "61-oversized-file.png");
        await Capture(FileNotices.EngineUnavailable(), "62-engine-timeout.png");
        await Capture(FileNotices.ReadOnlyDocument(), "63-readonly-file.png", () =>
        {
            var denied = FileNotices.ReadOnlyDocument(accessDenied: true);
            check(denied.MessageText == "이 문서에 쓰기 권한이 없습니다." && denied.DetailText == "이 창에서는 읽기 전용으로 엽니다.",
                "Figma 4874 read-only notice names the lock or permission cause in one line each");
            denied.Close();
        });
        await Capture(FileNotices.InvalidRange(31, 12, "시작이 끝보다 뒤입니다."), "64-invalid-range.png");
        await Capture(FileNotices.SaveFailed("화면 검증용 오류: 쓰기 권한 없음"), "70-save-failed.png");
        await Capture(FileNotices.PartialConversion(128, 125, [12, 47, 98]), "79-partial-conversion.png");
        await Capture(FileNotices.ConversionFailed(), "80-conversion-failed.png");
        await Capture(FileNotices.Disconnected(47), "81-disconnected.png");
        await Capture(FileNotices.Offline("로컬 화면 검증용 연결 실패"), "82-offline.png");
        await Capture(FileNotices.NoCredits(), "83-no-credits.png");
        await Capture(new MergeBlocksDialog(), "84-merge-blocks.png");
        var unreadableModel = new DocumentSession();
        unreadableModel.Add(new EditorBlock { Id = 1, Page = 3, Source = "첫 줄 ■ 둘째", Metadata = new(IsBlocked: true) });
        unreadableModel.Add(new EditorBlock { Id = 2, Page = 4, Source = "읽은 글 ■ 와 ■ 자리" });
        check(UnreadableText.Count(unreadableModel) == 2 && UnreadableText.First(unreadableModel) is { Block.Id: 2, Offset: 5 },
            "Figma 4844 unreadable spots count ■ markers outside blocked blocks and locate the first one");
        var unreadable = FileNotices.UnreadableCharacters(2);
        await Capture(unreadable, "86-unreadable-characters.png", () => check(unreadable.ActionButtons[0].IsDefault && Math.Abs(unreadable.ActionButtons[0].ActualWidth - unreadable.ActionButtons[1].ActualWidth) <= 2,
            "Unreadable-character notice defaults to the location action with equal Figma button widths"));
        var cancelMerge = new MergeBlocksDialog();
        check(Modal(cancelMerge, () => Key(cancelMerge, cancelMerge, System.Windows.Input.Key.Escape)) != true && cancelMerge.Decision == ManualEditDecision.Cancel,
            "Merge confirmation Escape cancels without authorizing manual-braille replacement");
        var keepMerge = new MergeBlocksDialog();
        check(Modal(keepMerge, () => keepMerge.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))) == true && keepMerge.Decision == ManualEditDecision.Keep,
            "Merge confirmation retains the existing keep-manual-braille option");
        var partialNotice = FileNotices.PartialConversion(128, 125, [12, 47, 98]);
        check(partialNotice.ActionButtons[0].IsEnabled && partialNotice.ActionButtons[0].IsDefault &&
            Modal(partialNotice, () => partialNotice.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))) == true && partialNotice.Choice == 1,
            "Partial conversion offers the server retry (BE 2026-10-27) and still keeps completed pages when opened as is");
        var offlineRetry = FileNotices.Offline("fixture");
        check(Modal(offlineRetry, () => offlineRetry.ActionButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))) == true && offlineRetry.Choice == 0,
            "Offline retry returns the retry action to the command handler");
        var offlineContinue = FileNotices.Offline("fixture");
        check(Modal(offlineContinue, () => offlineContinue.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))) == true && offlineContinue.Choice == 1,
            "Offline continue returns without requesting command replay");
        var failedSave = FileNotices.SaveFailed("fixture");
        check(Modal(failedSave, () => Key(failedSave, failedSave, System.Windows.Input.Key.Escape)) != true && failedSave.Choice == -1,
            "Save failure Escape preserves the unsaved document without triggering another save");
        var rejected = FileNotices.LoginRejected(new(System.Net.HttpStatusCode.Unauthorized, "AUTH4001", "아이디 또는 비밀번호가 맞지 않습니다.", System.Text.Json.JsonSerializer.SerializeToElement(new { remainingAttempts = 2 })));
        await Capture(rejected, "71-login-rejected.png", () => check(rejected.DetailText == "남은 횟수 2회입니다. 5회 실패하면 10분 동안 잠깁니다." && Math.Abs(rejected.ActionButtons[0].ActualWidth - rejected.ActionButtons[1].ActualWidth) <= 2,
            "Login rejection states the lock policy with equal Figma button widths"));
        var alternativeBlock = new EditorBlock { Metadata = new(Type: "caption", SelectedIndex: 2, Alternatives: [
            new("생략", "", null, "", "설명 생략"), new("짧은 제목", "네 지역의 기후 특성", null, "⠁", "간략한 제목"),
            new("개조식", "그림: 네 지역의 기후 특성\n1. (가) 최한월 −6℃, 연강수량 1,300㎜\n2. (나) 최한월 −2℃, 연강수량 1,100㎜", null, "⠁⠃⠉", "항목별 설명"),
            new("줄글", "네 지역의 기후 특성을 비교한 그림입니다.", null, "⠙", "연결된 문장")]) };
        var alternatives = new AlternativeDialog(alternativeBlock, (_, _, _) => throw new InvalidOperationException("Legacy fixture drafts require no translation request."));
        await Capture(alternatives, "56-alternative-drafts.png", () =>
        {
            check(alternatives.SelectedIndex == 2 && alternatives.Translation?.Cells == "⠁⠃⠉", "Draft popover restores the selected draft and its braille preview");
            alternatives.DraftChoices[3].IsChecked = true;
            check(alternatives.SelectedIndex == 3 && alternatives.Translation?.Cells == "⠙" && alternativeBlock.Metadata.Alternatives!.Length == 4 && alternativeBlock.Metadata.SelectedIndex == 2,
                "Draft selection previews a different draft without deleting alternatives or committing to the document before confirmation");
        });
        var acceptDraft = new AlternativeDialog(alternativeBlock, (_, _, _) => throw new InvalidOperationException());
        check(Modal(acceptDraft, () => { acceptDraft.DraftChoices[3].IsChecked = true; Key(acceptDraft, acceptDraft, System.Windows.Input.Key.Enter); }) == true &&
            acceptDraft.SelectedIndex == 3 && acceptDraft.Translation?.Cells == "⠙" && alternativeBlock.Metadata.SelectedIndex == 2,
            "Draft Enter returns the selected draft for caller confirmation without changing the document early");
        var cancelDraft = new AlternativeDialog(alternativeBlock, (_, _, _) => throw new InvalidOperationException());
        check(Modal(cancelDraft, () => { cancelDraft.DraftChoices[3].IsChecked = true; Key(cancelDraft, cancelDraft, System.Windows.Input.Key.Escape); }) != true &&
            alternativeBlock.Metadata.SelectedIndex == 2 && alternativeBlock.Metadata.Alternatives!.Length == 4,
            "Draft Escape preserves the previous selection and every alternative");
        await Capture(new NoticeDialog("변경 내용을 저장하시겠습니까?", "저장하지 않으면 지금까지 고친 것이 사라집니다. 중간 저장은 하지 않습니다.", ["저장", "저장 안 함", "취소"], width: 591), "55-unsaved-confirmation.png");
        await Capture(new AppliedRulesDialog(api, [new("( 여는 괄호", "⠦", "제23항", "문장부호", "소괄호표는 여는 때 ⠦, 닫는 때 ⠴로 적는다.", "한국 점자 규정", "symbol"), new("~ 물결표", "⠤", "제38항", "문장부호", "물결표는 ⠤로 적고 앞말과 뒷말에 붙여 쓴다.", "한국 점자 규정", "symbol")], false, "(가)~(라)", "⠦⠫⠴⠭⠦⠐⠣⠴", "7줄 3~12칸"), "51-applied-rules.png");
        var blockEvidence = new AppliedRulesDialog(api,
            [new("원본 자료 (원문)", "", "제1항", "원본 근거", "원본 변환의 근거", "규정", "fixture", true)],
            false, "원본", "⠁", "1줄 1칸");
        await Capture(blockEvidence, "51-applied-rules-block-evidence.png", () =>
            check(Children(blockEvidence).OfType<TextBlock>().Any(t => t.IsVisible
                && t.Text.Contains("점자 칸에 적용되는 위치는 확인되지 않았습니다", StringComparison.Ordinal)),
                "Server evidence without verified coordinates is explicitly identified in the rules dialog"));
        var expandedRules = new RuleDialog(api, [], "", "", "음악") { Owner = owner }; expandedRules.Show();
        try { await render(expandedRules, Path.Combine(directory, "52-rules-expanded.png")); ((Expander)expandedRules.RuleResults.Children[0]).IsExpanded = true; await render(expandedRules, Path.Combine(directory, "52-rules-expanded.png")); } finally { expandedRules.Close(); }
        var keys = new SixDotDialog("F D S J K L", true, true);
        await Capture(keys, "39-six-keys.png", () => check(keys.KeyFields.Length == 6 && keys.KeyFields.All(k => k.IsVisible && k.MaxLength == 1), "QA4 six-dot mapping exposes six individual editable keys"));
        var help = new HelpWindow("점자 직접 고치기");
        await Capture(help, "40-help-tree.png", () => check(Children(help).OfType<TabItem>().Select(t => t.Tag as string).SequenceEqual(["separator", "separator", null])
            && Children(help).OfType<FlowDocumentScrollViewer>().Single().Document.TextAlignment == TextAlignment.Left,
            "Figma 4641 help tabs read 내용 | 색인 | 검색 and the topic body is left-aligned"));
        var editor = new MainWindow(smoke: true); editor.Show();
        try
        {
            var first = editor.Session.Blocks[0]; var second = editor.Session.Blocks[1];
            first.Source = "<!2칸>두 칸\n<!4칸>네 칸"; second.Source = "검색할 문장\n메모 줄";
            second.Memos.Add(new("page-memo", null, "TEXT", 1, "메모 줄", "연속 문서의 메모", DateTimeOffset.Now, Author: "검수자"));
            editor.Session.ActiveBlock = first; editor.PageEditor.Refresh();
            await render(editor, Path.Combine(directory, "41-source-indents.png"));
            var lines = editor.PageEditor.Editor.TextArea.TextView.VisualLines;
            check(lines.Take(2).All(l => l.Elements.Any(e => e.DocumentLength == 0)) && first.Source.StartsWith("<!2칸>"), "QA4 indentation tags create display-only space without changing source bytes");
            await editor.FindNextAsync("검색할", SearchPanel.Text);
            check(editor.Session.ActiveBlock == second && editor.PageEditor.Editor.SelectedText == "검색할", "QA4 source search selects the visible continuous editor at the correct block offset");
            var globalLine = first.Source.Count(c => c == '\n') + 2;
            var resolved = editor.PageEditor.ResolveLine(globalLine);
            check(resolved.Block == second && resolved.Line == 1, "QA4 continuous memo line resolves to its owning block and local line");
            var margin = editor.PageEditor.Editor.TextArea.LeftMargins.OfType<Controls.MemoMargin>().Single();
            check(margin.Preview(globalLine).Contains("검수자") && margin.Preview(globalLine).Contains("연속 문서의 메모"), "QA4 hover text follows memos in other visible source blocks");
            await editor.ReplaceMatchesAsync("검색할", "고친", SearchPanel.Text, false); editor.PageEditor.Refresh();
            check(second.Source.StartsWith("고친 문장") && editor.PageEditor.Editor.Text.Contains("고친 문장"), "QA4 replace changes the matched source block and visible page together");
            editor.Session.Blocks.Clear();
            var tagsBlock = new EditorBlock { Id = 1, Page = 1, Source = "2 (한국지리) 사회탐구 영역\n<!꼬리말><!/꼬리말>\n6. 그래프는 지도에 표시된 네 지역의\n<!강조>기후 특성<!/강조>을 나타낸 것이다.\n<!주>그림: 네 지점 표시<!/주>\n<!상자>\n① (가)는 겨울철 강수량이 많다.\n<!상자끝>" };
            editor.Session.Add(tagsBlock); editor.Session.ActiveBlock = tagsBlock; editor.PageEditor.Refresh();
            editor.SourcePanel.Width = 640; editor.SourcePanel.Height = 300; editor.SourcePanel.HorizontalAlignment = HorizontalAlignment.Left; editor.SourcePanel.VerticalAlignment = VerticalAlignment.Top;
            editor.PageEditor.FollowActive(tagsBlock.Source.IndexOf("<!/꼬리말>"), 0); editor.PageEditor.Editor.Focus();
            await WindowsSmoke.CaptureComponentAsync(editor.SourcePanel, editor, 640, 300, Path.Combine(directory, "67-tag-panel.png"));
            editor.SourcePanel.Width = editor.SourcePanel.Height = double.NaN; editor.SourcePanel.HorizontalAlignment = HorizontalAlignment.Stretch; editor.SourcePanel.VerticalAlignment = VerticalAlignment.Stretch;
            tagsBlock.Source = "2026학년도 수능 안내\n시험장은 오전 8시 10분까지 들어가야 한다.\n수험표와 신분증을 반드시 가져온다.\n점심 도시락은 각자 준비한다.\n(안내문 끝)";
            tagsBlock.Braille = "⠼⠃⠚⠃⠋⠚⠁⠈⠘⠉⠀⠠⠍⠙⠻⠀⠣⠒⠎\n⠠⠎⠢⠀⠕⠿⠀⠫⠙⠔⠲";
            editor.Session.Title = "안내문.txt"; editor.Session.IsTextJob = true; editor.HidePanel("hideOriginal"); editor.LinePreviewToggle.IsChecked = false; editor.LineStrip.Visibility = Visibility.Collapsed;
            editor.PageEditor.Refresh(); editor.PageEditor.FollowActive(tagsBlock.Source.IndexOf("수험표"), 0); editor.PageEditor.Editor.Focus();
            await render(editor, Path.Combine(directory, "68-text-file.png"));
            check(editor.SourcePanelNumber.Text == "1" && editor.Braille.PanelNumber.Text == "2" && Math.Abs(editor.SourcePanel.ActualWidth / editor.Braille.ActualWidth - 854.0 / 586.0) < .02,
                "TXT layout uses the final Figma panel ratio and two-panel numbering");
            editor.Braille.Width = 820; editor.Braille.Height = 420; editor.Braille.HorizontalAlignment = HorizontalAlignment.Left; editor.Braille.VerticalAlignment = VerticalAlignment.Top;
            editor.Braille.GridView.ShowOutlines = false; editor.Braille.GridView.InvalidateFaces(); editor.Braille.PanelNumber.Text = "3";
            editor.Braille.SetLinePreviewVisible(true); editor.Braille.SetLinePreview(3, tagsBlock.Braille.Split('\n')[0]); editor.Braille.GridView.Focus();
            await WindowsSmoke.CaptureComponentAsync(editor.Braille, editor, 820, 420, Path.Combine(directory, "69-braille-panel.png"));
        }
        finally { editor.PrepareSmokeShutdown(); editor.Close(); }
    }
    internal static void CheckBrailleSurface(MainWindow owner, Action<bool, string> check, Action<string> stage)
    {
        var brailleGrid = owner.Braille.GridView;
        var brailleContainer = VisualTreeHelper.GetParent(brailleGrid) as Grid;
        var rowIndex = Grid.GetRow(brailleGrid);
        var rowHeight = brailleContainer is not null && rowIndex < brailleContainer.RowDefinitions.Count
            ? brailleContainer.RowDefinitions[rowIndex].ActualHeight : 0;
        var tolerance = Math.Max(1, 1 / VisualTreeHelper.GetDpi(brailleGrid).DpiScaleY);
        check(owner.Braille.Editor.Visibility == Visibility.Collapsed,
            "The duplicate braille model editor remains hidden");
        check(brailleGrid.IsVisible && brailleGrid.IsEnabled && brailleGrid.Focusable && brailleGrid.ActualHeight > 0,
            "The braille grid is visible, enabled and keyboard-focusable");
        check(brailleContainer is not null && rowHeight > 0 && Math.Abs(brailleGrid.ActualHeight - rowHeight) <= tolerance,
            $"The braille grid fills its available layout row (window={owner.ActualHeight:F1}, row={rowHeight:F1}, grid={brailleGrid.ActualHeight:F1})");
        if (owner.ActualHeight >= 950)
            check(brailleGrid.ActualHeight > 500,
                $"A large editor window retains a full-height braille grid (window={owner.ActualHeight:F1}, grid={brailleGrid.ActualHeight:F1})");
        else
            stage($"large braille window check skipped: actual window height {owner.ActualHeight:F1}");
    }

    private static async Task CheckViewCommandsAsync(MainWindow owner, Action<bool, string> check)
    {
        var content = owner.Session.Blocks.Select(b => (b.Source, b.Braille)).ToArray(); var dirty = owner.Session.IsDirty;
        owner.ToolbarToggle.IsChecked = false; await owner.ExecuteAsync("showToolbar");
        check(owner.EditorToolbar.Visibility == Visibility.Collapsed, "View toolbar action hides the toolbar");
        owner.ToolbarToggle.IsChecked = true; await owner.ExecuteAsync("showToolbar");
        owner.StatusToggle.IsChecked = false; await owner.ExecuteAsync("showStatus");
        check(owner.StatusBar.Visibility == Visibility.Collapsed, "View status action hides the status bar");
        owner.StatusToggle.IsChecked = true; await owner.ExecuteAsync("showStatus");
        var margin = owner.PageEditor.Editor.TextArea.LeftMargins.OfType<Controls.MemoMargin>().Single();
        var width = margin.GutterWidth;
        owner.MemoToggle.IsChecked = false; await owner.ExecuteAsync("showMemos");
        owner.ReviewToggle.IsChecked = false; await owner.ExecuteAsync("showReviews");
        check(!margin.ShowMemos && !margin.ShowReviews && margin.GutterWidth == width && margin.Visibility == Visibility.Visible,
            "View memo and review switches preserve editor text alignment");
        owner.MemoToggle.IsChecked = owner.ReviewToggle.IsChecked = true;
        await owner.ExecuteAsync("showMemos"); await owner.ExecuteAsync("showReviews");
        var previousSixDot = owner.Braille.SixDotEnabled;
        var shortcut = owner.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.F6);
        shortcut.Command.Execute(shortcut.CommandParameter);
        check(owner.Braille.SixDotEnabled != previousSixDot, "F6 still toggles actual six-dot input after menu alignment");
        shortcut.Command.Execute(shortcut.CommandParameter);
        await owner.ExecuteAsync("detachSource"); await owner.ExecuteAsync("detachOriginal");
        check(owner.DetachedSourceWindow is not null && owner.DetachedOriginalWindow is not null, "View restore fixture detaches two real shared-document panels");
        owner.BrailleToggle.IsChecked = false; await owner.ExecuteAsync("restorePanels"); owner.UpdateLayout();
        check(owner.DetachedSourceWindow is null && owner.DetachedOriginalWindow is null && owner.OriginalPanel.IsVisible && owner.SourcePanel.IsVisible && owner.Braille.IsVisible,
            "View restore closes detached windows and restores all three panels");
        check(owner.Session.IsDirty == dirty && content.SequenceEqual(owner.Session.Blocks.Select(b => (b.Source, b.Braille))),
            "View commands leave source text, braille and document dirty state unchanged");
        string? invoked = null;
        var help = new HelpWindow("추가 도구", command => { invoked = command; return Task.CompletedTask; }) { Owner = owner };
        help.Show();
        try
        {
            help.UpdateLayout();
            var reader = Children(help).OfType<FlowDocumentScrollViewer>().Single();
            var links = reader.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Hyperlink>()).ToArray();
            links[0].RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
            check(links.Length == 27 && invoked == "sixDotMode", "Help additional tools retain executable access to actions removed from main menus");
            var tree = Children(help).OfType<TreeView>().Single();
            var branch = tree.Items.OfType<TreeViewItem>().First(b => b.Header.ToString() == "메뉴별 기능");
            branch.IsExpanded = true;
            var topic = branch.Items.OfType<TreeViewItem>().First(t => t.Header.ToString() == "대체 초안과 규정"); topic.IsSelected = true;
            check(reader.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First().Inlines.OfType<System.Windows.Documents.Run>().First().Text.EndsWith("대체 초안과 규정", StringComparison.Ordinal),
                "Help tree opens retained draft and rule guidance after navigation restyling");
        }
        finally { help.Close(); }
    }
    private static IEnumerable<string> GlyphFonts(Visual root)
    {
        IEnumerable<string> Read(Drawing? drawing)
        {
            if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun.GlyphTypeface.FontUri.ToString();
            if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var font in Read(child)) yield return font;
        }
        foreach (var font in Read(VisualTreeHelper.GetDrawing(root))) yield return font;
        foreach (var child in Children(root).OfType<Visual>()) foreach (var font in Read(VisualTreeHelper.GetDrawing(child))) yield return font;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Children(child)) yield return descendant; }
    }
    private sealed class Fixture : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            object Form(string id, bool builtin, string? name = null) => new { formId = id, name = name ?? (builtin ? "시험지" : "사용자 검수 양식"), builtin, baseFormId = builtin ? null : "builtin:EXAM", layoutOptions = JobClient.LayoutBody(new()), brailleOptions = JobClient.OptionsBody(new()) };
            object result = path switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER", alias = "검수자" },
                "/api/users/braille-forms" => new { items = new[] { Form("builtin:EXAM", true), Form("user:1", false), Form("builtin:TEXTBOOK", true, "중·고 교과서") } },
                "/api/users/braille-options" => new { formId = "builtin:EXAM", pageNumberLine = "odd", hangulGrade = "grade2", englishGrade = "grade1", visualDescriptions = VisualDescriptions.Defaults() },
                "/api/users/usage/daily" => new { from = "2026-10-01", to = "2026-10-27", orgRemaining = 8251, orgAllocated = 10000, usedCredits = 1749, usedPercent = 17.49,
                    daily = Enumerable.Range(1, 27).Select(day => new { date = $"2026-10-{day:00}", credits = day == 20 ? 600 : day == 21 ? 1149 : 0 }).ToArray(),
                    bySource = new[] { new { source = "PDF_HWP", credits = 1400 }, new { source = "IMAGE", credits = 349 }, new { source = "TEXT", credits = 0 } } },
                "/api/rules" => new { totalPages = 1, totalElements = 2, items = new object[] { new { ruleId = "music", source = "한국 점자 규정", section = "음악", ruleName = "음악 기호", contents = "요약", fullText = "첫 줄\n  ⠁⠃ 원문\n마지막 줄", supported = false }, new { ruleId = "symbol", source = "한국 점자 규정", section = "기호", ruleName = "기호 규정", contents = "기호 본문", fullText = "전문" } } },
                "/api/board/posts" => new BoardPage(1, 3, [new(0, "10월 정기 점검 안내", 0, "운영자", false, "10. 01.", 412, "fixture")], [new(127, "수식 변환이 안 되는 쪽이 있습니다", 2, "test***", false, "10. 07.", 31), new(126, "크레딧 추가 요청드립니다", 1, "test***", true, "10. 06.", 4), new(125, "HWP 파일이 열리지 않습니다", 0, "kbl***", false, "10. 05.", 18)]),
                "/api/board/notifications" => new BoardNotifications(2, [new(1, 126, "크레딧 추가 요청드립니다", 1, "10. 06. 17:40", false), new(2, 127, "수식 변환이 안 되는 쪽이 있습니다", 2, "10. 07. 09:12", false)]),
                _ => throw new InvalidOperationException("Unexpected UI fixture request: " + path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, code = "COMMON2000", result }), Encoding.UTF8, "application/json") });
        }
    }
}
