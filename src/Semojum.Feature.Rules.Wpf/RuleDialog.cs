using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public sealed class RuleDialog : CompactDialog
{
    internal StackPanel RuleResults { get; } = new();
    public RuleDialog(SemojumApiClient api, RuleEvidence[] evidence, string selectedSource, string selectedBraille,
        string? initialQuery = null) : base("점자 규정 찾아보기", 720, 748, popover: true)
    {
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = FigmaDialogElements.Brush("#EAEAEA"); frame.Background = FigmaDialogElements.Brush("#F3F3F3"); }
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new(56) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new(64) }); Body(layout);
        var header = new Grid { Margin = new(24, 0, 24, 0) };
        header.Children.Add(new TextBlock { Text = "점자 규정 찾아보기", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var dismiss = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right, Content = FigmaDialogElements.DesignIcon("rule-browser-close.svg", 16, 16, "Semojum.Feature.Rules.Wpf"), Template = FigmaDialogElements.SurfaceTemplate(0) };
        AutomationProperties.SetName(dismiss, "닫기"); dismiss.Click += (_, _) => Close(); header.Children.Add(dismiss); layout.Children.Add(header);
        var root = new DockPanel { Margin = new(16, 16, 16, 12) }; Grid.SetRow(root, 1); layout.Children.Add(root);
        if (initialQuery is null && (selectedSource.Length > 0 || selectedBraille.Length > 0))
        {
            var excerpt = new TextBlock { Text = $"묵자: {selectedSource}\n점자: {selectedBraille}", TextWrapping = TextWrapping.Wrap, MaxHeight = 64, Margin = new(0, 0, 0, 10) };
            DockPanel.SetDock(excerpt, Dock.Top); root.Children.Add(excerpt);
        }
        var filters = new StackPanel { Margin = new(0, 0, 0, 16) }; DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
        var query = new TextBox { Text = initialQuery ?? "", MaxLength = 100, Height = 32, Padding = new(31, 0, 7, 0), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(query, "규정 검색어");
        var document = Choices(["전체 문서", "한국 점자 규정", "점자 도서 제작 지침", "점자 자료 제작 지침"], "전체 문서");
        AutomationProperties.SetName(document, "규정 문서 필터");

        var pagination = new DockPanel { Height = 32, Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(pagination, Dock.Bottom); root.Children.Add(pagination);
        var resultCount = new TextBlock { Foreground = FigmaDialogElements.Brush("#5F5F5F"), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(resultCount, Dock.Left); pagination.Children.Add(resultCount);
        var pages = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(pages, Dock.Right); pagination.Children.Add(pages);
        var page = 0; var totalPages = 1; var pageInput = new TextBox { Text = "1", Width = 44, Height = 24, Padding = new(7, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var results = RuleResults; var cardStyle = CreateRuleCardStyle();
        AutomationProperties.SetName(pageInput, "규정 검색 페이지");
        var pageTotal = new TextBlock { Text = "/ 1", Margin = new(8, 0, 8, 0), Foreground = FigmaDialogElements.Brush("#5F5F5F") };
        Button previous = null!, next = null!;
        previous = Button("이전", () => RunAsync(async () => { if (page > 0) { page--; await SearchAsync(); } }));
        next = Button("다음", () => RunAsync(async () => { if (page + 1 < totalPages) { page++; await SearchAsync(); } }));
        foreach (var button in new[] { previous, next })
        { button.Width = 48; button.MinWidth = 0; button.Height = 24; button.MinHeight = 0; button.Padding = new(0); button.Margin = new(0); button.Background = FigmaDialogElements.Brush("#FBFBFB"); button.BorderBrush = FigmaDialogElements.Brush("#EAEAEA"); button.Template = FigmaDialogElements.SurfaceTemplate(4); }
        AutomationProperties.SetName(previous, "이전 쪽"); AutomationProperties.SetName(next, "다음 쪽");
        pages.Children.Add(previous); pageInput.Margin = new(8, 0, 0, 0); pages.Children.Add(pageInput); pages.Children.Add(pageTotal); pages.Children.Add(next);

        root.Children.Add(new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        void Display(IEnumerable<BrailleRule> rows)
        {
            results.Children.Clear(); Expander? expanded = null;
            foreach (var row in rows)
            {
                var cardHeader = new StackPanel();
                cardHeader.Children.Add(new TextBlock { Text = row.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) });
                cardHeader.Children.Add(new TextBlock { Text = row.Contents, MaxHeight = 36, LineHeight = 18, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
                if (row.Supported == false) cardHeader.Children.Add(new TextBlock { Text = row.SupportLabel, Foreground = Brushes.DarkRed });
                var expandedContent = new StackPanel { Margin = new(9, 5, 9, 8) };
                expandedContent.Children.Add(new TextBlock { Text = row.HasFullText ? "조항 원문" : "발췌 · 전문 미제공", Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
                expandedContent.Children.Add(new TextBlock { Text = row.ExpandedText, TextWrapping = TextWrapping.Wrap });
                if (!row.HasFullText && row.RuleId.Length > 0)
                    expandedContent.Children.Add(Button("규정 전문 찾기", () => RunAsync(async () => { query.Text = row.RuleId; document.SelectedIndex = 0; page = 0; await SearchAsync(); })));
                var item = new Expander { Header = cardHeader, Content = new Border { Background = Brushes.White, BorderBrush = FigmaDialogElements.Brush("#EAEAEA"), BorderThickness = new(1), Child = expandedContent }, Tag = row.Source + " · " + row.Section, Margin = new(0, 0, 0, 8), Style = cardStyle };
                AutomationProperties.SetName(item, row.Section + " " + row.Name);
                item.Expanded += (_, _) => { if (expanded != item) { if (expanded is not null) expanded.IsExpanded = false; expanded = item; } };
                results.Children.Add(item);
            }
            if (results.Children.Count == 0) results.Children.Add(new TextBlock { Text = "검색 결과가 없습니다.", Margin = new(8) });
            else if (results.Children[0] is Expander first) first.IsExpanded = true;
        }
        async Task SearchAsync()
        {
            var code = document.SelectedIndex switch { 1 => "MCST", 2 => "NLD", 3 => "NISE", _ => "" };
            var data = await new RuleClient(api).SearchAsync(query.Text, code, page, Lifetime.Token);
            totalPages = Math.Max(1, data.TotalPages);
            Display(data.Items);
            pageInput.IsEnabled = true;
            previous.IsEnabled = page > 0; next.IsEnabled = page + 1 < totalPages;
            pageInput.Text = (page + 1).ToString(); pageTotal.Text = "/ " + totalPages;
            var start = data.TotalElements == 0 ? 0 : page * 20 + 1; var end = Math.Min(data.TotalElements, (page + 1) * 20);
            resultCount.Text = $"{data.TotalElements}건 중 {start}–{end}";
        }
        pageInput.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return; e.Handled = true;
            if (!int.TryParse(pageInput.Text, out var target) || target < 1 || target > totalPages)
            { Status.Text = $"1–{totalPages}쪽을 입력해 주세요."; return; }
            await RunAsync(async () => { page = target - 1; await SearchAsync(); });
        };
        var search = Button("검색", () => RunAsync(async () => { page = 0; await SearchAsync(); }));
        var searchHost = new Grid(); searchHost.Children.Add(query);
        var placeholder = new TextBlock { Text = "점역 규정 검색...", Foreground = FigmaDialogElements.Brush("#8A8A8A"), Margin = new(31, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        AutomationProperties.SetAutomationId(placeholder, "RuleSearchPlaceholder");
        void UpdatePlaceholder() => placeholder.Visibility = query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        query.TextChanged += (_, _) => UpdatePlaceholder(); UpdatePlaceholder(); searchHost.Children.Add(placeholder);
        search.MinWidth = search.Width = 24; search.Height = 24; search.MinHeight = 0; search.Margin = new(4, 0, 0, 0); search.HorizontalAlignment = HorizontalAlignment.Left;
        search.Content = FigmaDialogElements.DesignIcon("rule-browser-book.svg", 16, 16, "Semojum.Feature.Rules.Wpf"); search.Background = Brushes.Transparent; search.BorderBrush = Brushes.Transparent; search.Template = FigmaDialogElements.SurfaceTemplate(0); search.Padding = new(0);
        AutomationProperties.SetName(search, "규정 검색"); searchHost.Children.Add(search); filters.Children.Add(searchHost);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 16, 0, 0) }; filters.Children.Add(chips);
        var labels = new[] { "전체", "한국 점자 규정", "점자 도서 제작 지침", "점자 자료 제작 지침" };
        var widths = new[] { 44d, 106, 136, 136 };
        void PaintChips()
        {
            for (var i = 0; i < chips.Children.Count; i++)
            {
                var chip = (Button)chips.Children[i]; chip.Background = i == document.SelectedIndex ? FigmaDialogElements.Brush("#EFF6FC") : Brushes.White;
                chip.BorderBrush = FigmaDialogElements.Brush(i == document.SelectedIndex ? "#005FB8" : "#D1D1D1");
                AutomationProperties.SetHelpText(chip, i == document.SelectedIndex ? "선택됨" : "");
            }
        }
        for (var i = 0; i < labels.Length; i++)
        {
            var selection = i; var chip = Button(labels[i], () => { document.SelectedIndex = selection; PaintChips(); return Task.CompletedTask; });
            chip.MinWidth = chip.Width = widths[i]; chip.Height = chip.MinHeight = 24; chip.Margin = new(0, 0, 8, 0); chip.Padding = new(0); chips.Children.Add(chip);
        }
        PaintChips();
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunAsync(async () => { page = 0; await SearchAsync(); }); } };
        document.SelectionChanged += async (_, _) => await RunAsync(async () => { page = 0; await SearchAsync(); });
        void AppliedRules()
        {
            Display(evidence.Select(r => new BrailleRule(r.RuleId, r.Source, r.Section, r.Title, r.Excerpt)));
            previous.IsEnabled = next.IsEnabled = false; pageInput.IsEnabled = false;
            Status.Text = evidence.Length == 0 ? "이 블록에 전달된 적용 규정이 없습니다." : "원문 수신 당시의 적용 규정입니다. 검색으로 현재 본문을 확인할 수 있습니다.";
        }
        var context = new ContextMenu(); var appliedMenu = new MenuItem { Header = "적용 규정" };
        appliedMenu.Click += (_, _) => AppliedRules(); context.Items.Add(appliedMenu); root.ContextMenu = context;
        var footer = new Border { BorderBrush = FigmaDialogElements.Brush("#EAEAEA"), BorderThickness = new(0, 1, 0, 0) }; Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var footerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 15, 24, 16) }; footer.Child = footerActions;
        if (evidence.Length > 0)
        {
            var applied = Action("적용 규정", AppliedRules); Actions.Children.Remove(applied); applied.Width = 88; applied.MinWidth = 0; applied.Height = 32; applied.MinHeight = 0; applied.Template = FigmaDialogElements.SurfaceTemplate(4); footerActions.Children.Add(applied);
        }
        var close = Action("닫기", Close, true); Actions.Children.Remove(close); close.Width = 88; close.MinWidth = 0; close.Height = 32; close.MinHeight = 0; close.Template = FigmaDialogElements.SurfaceTemplate(4); footerActions.Children.Add(close);
        if (initialQuery is null && evidence.Length > 0) AppliedRules(); else OnLoad(SearchAsync);
    }

    private Style CreateRuleCardStyle()
    {
        var style = new Style(typeof(Expander), (Style)FindResource("RuleCard"));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton)); toggle.SetValue(Control.PaddingProperty, new Thickness(0)); toggle.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        toggle.SetValue(DockPanel.DockProperty, Dock.Top);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(Expander.IsExpanded)) { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
        var toggleBorder = new FrameworkElementFactory(typeof(Border)); toggleBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent); toggleBorder.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        toggle.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(ToggleButton)) { VisualTree = toggleBorder });
        var heading = new FrameworkElementFactory(typeof(DockPanel)); heading.SetValue(FrameworkElement.MarginProperty, new Thickness(11, 11, 11, 8)); heading.SetValue(FrameworkElement.MinHeightProperty, 60d);
        var arrow = new FrameworkElementFactory(typeof(SvgViewbox), "Arrow"); arrow.SetValue(SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Feature.Rules.Wpf;component/Assets/Figma/rule-browser-right.svg"));
        arrow.SetValue(FrameworkElement.WidthProperty, 16d); arrow.SetValue(FrameworkElement.HeightProperty, 16d); arrow.SetValue(Viewbox.StretchProperty, Stretch.None);
        arrow.SetValue(DockPanel.DockProperty, Dock.Right); arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0)); arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top); heading.AppendChild(arrow);
        var headingContent = new FrameworkElementFactory(typeof(ContentPresenter)); headingContent.SetValue(ContentPresenter.ContentSourceProperty, "Header"); heading.AppendChild(headingContent); toggle.AppendChild(heading); panel.AppendChild(toggle);
        var provenance = new FrameworkElementFactory(typeof(TextBlock)); provenance.SetValue(DockPanel.DockProperty, Dock.Bottom); provenance.SetBinding(TextBlock.TextProperty, new Binding(nameof(Tag)) { RelativeSource = RelativeSource.TemplatedParent });
        provenance.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); provenance.SetValue(TextBlock.ForegroundProperty, FigmaDialogElements.Brush("#8A8A8A")); provenance.SetValue(FrameworkElement.MarginProperty, new Thickness(11, 0, 11, 8)); panel.AppendChild(provenance);
        var details = new FrameworkElementFactory(typeof(ContentPresenter), "Details"); details.SetValue(ContentPresenter.ContentSourceProperty, "Content"); details.SetValue(FrameworkElement.MarginProperty, new Thickness(11, 0, 11, 4)); details.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); panel.AppendChild(details); border.AppendChild(panel);
        var template = new ControlTemplate(typeof(Expander)) { VisualTree = border };
        var expanded = new Trigger { Property = Expander.IsExpandedProperty, Value = true };
        expanded.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Details"));
        expanded.Setters.Add(new Setter(SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Feature.Rules.Wpf;component/Assets/Figma/rule-browser-down.svg"), "Arrow"));
        template.Triggers.Add(expanded); style.Setters.Add(new Setter(TemplateProperty, template)); style.Setters.Add(new Setter(BorderBrushProperty, FigmaDialogElements.Brush("#EAEAEA")));
        var open = new Trigger { Property = Expander.IsExpandedProperty, Value = true };
        open.Setters.Add(new Setter(BackgroundProperty, FigmaDialogElements.Brush("#EFF6FC"))); open.Setters.Add(new Setter(BorderBrushProperty, FigmaDialogElements.Brush("#005FB8"))); style.Triggers.Add(open);
        return style;
    }
}
