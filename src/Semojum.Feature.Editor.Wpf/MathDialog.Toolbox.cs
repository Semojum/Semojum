using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfMath.Controls;

namespace Semojum.Desktop.Dialogs;

public sealed partial class MathDialog
{
    internal WrapPanel TemplateButtons { get; } = new();

    private FrameworkElement BuildToolbox()
    {
        var panel = new StackPanel();
        var border = new Border
        {
            Background = EditorModalDesign.Color("#F9F9F9"), BorderBrush = EditorModalDesign.Color("#D1D1D1"),
            BorderThickness = new(1), Margin = new(0, 0, 0, 8), Padding = new(6), Child = panel
        };
        SymbolButtons.Margin = new(0, 0, 0, 5);
        panel.Children.Add(SymbolButtons);
        var all = MathTemplates.Groups.SelectMany(group => group.Templates).ToArray();
        foreach (var (icon, label) in new[] {
            ("⅟", "분수"), ("√", "제곱근"), ("x²", "거듭제곱"), ("x₂", "아래 첨자"),
            ("∑", "합"), ("∏", "곱"), ("∫", "정적분"), ("lim", "극한"),
            ("( )", "소괄호"), ("[ ]", "대괄호"), ("{ }", "중괄호"), ("▦", "2×2 소괄호 행렬"),
            ("±", "플러스마이너스"), ("≤", "작거나 같음"), ("∈", "원소"), ("→", "오른쪽 화살표"),
            ("α", "알파"), ("sin", "사인"), ("∠", "각"), ("v⃗", "벡터"),
            ("⋯", "줄임표"), ("㎡", "제곱미터"), ("H₂O", "화학식") })
        {
            var template = all.Single(item => item.Label == label);
            var button = new Button
            {
                Content = icon, Width = 36, Height = 32, MinWidth = 0, MinHeight = 0, Margin = new(0, 0, 3, 4),
                Padding = new(0), ToolTip = label, Background = Brushes.White, BorderBrush = EditorModalDesign.Color("#D1D1D1")
            };
            if (label == "벡터") button.FontFamily = new("Cambria Math");
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { HighlightSymbol(button); InsertTemplate(template); };
            SymbolButtons.Children.Add(button);
        }
        panel.Children.Add(new Border { BorderBrush = EditorModalDesign.Color("#D1D1D1"), BorderThickness = new(0, 1, 0, 0), Margin = new(0, 0, 0, 5) });
        panel.Children.Add(TemplateButtons);
        foreach (var group in MathTemplates.Groups)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = group.Icon, FontFamily = new("Cambria Math"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = group.Label + " ▾", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button
            {
                Content = content, Width = 74, Height = 60, MinWidth = 0, MinHeight = 0, Padding = new(2), Margin = new(0, 0, 2, 4),
                Background = Brushes.White, BorderBrush = EditorModalDesign.Color("#D1D1D1"), ToolTip = group.Label + " 수식 틀 선택"
            };
            AutomationProperties.SetName(button, group.Label + " 수식 틀");
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, MaxHeight = 480 };
            foreach (var template in group.Templates)
            {
                var item = new MenuItem { Header = template.Label, Tag = template, ToolTip = template.Insert().Text };
                AutomationProperties.SetName(item, template.Label);
                item.Click += (_, _) => { HighlightSymbol(null); InsertTemplate(template); };
                menu.Items.Add(item);
            }
            var illustrated = false;
            menu.Opened += (_, _) =>
            {
                if (illustrated) return;
                illustrated = true;
                foreach (MenuItem item in menu.Items)
                {
                    var template = (MathTemplate)item.Tag;
                    var header = new Grid();
                    header.ColumnDefinitions.Add(new() { Width = new(130) });
                    header.ColumnDefinitions.Add(new() { Width = new(170) });
                    header.Children.Add(new TextBlock { Text = template.Label, VerticalAlignment = VerticalAlignment.Center });
                    try
                    {
                        var preview = new FormulaControl { SystemTextFontName = "Malgun Gothic", Formula = template.Insert().Text, FontSize = 20, Margin = new(4) };
                        if (preview.HasError) continue;
                        var box = new Viewbox { Child = preview, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = 64 };
                        Grid.SetColumn(box, 1); header.Children.Add(box); item.Header = header;
                    }
                    catch { /* Keep the accessible text choice if a font cannot render the preview. */ }
                }
            };
            void OpenMenu() { menu.IsOpen = true; }
            button.ContextMenu = menu;
            button.Click += (_, _) => OpenMenu();
            button.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Down) return;
                OpenMenu(); e.Handled = true;
            };
            TemplateButtons.Children.Add(button);
        }
        panel.Children.Add(new TextBlock
        {
            Text = "종류별 버튼에서 수식 틀을 선택하세요. 선택한 내용은 첫 항목에 들어가며, 다음 항목을 바로 수정할 수 있습니다.",
            Foreground = (Brush)FindResource("Muted"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(2, 3, 2, 2)
        });
        return border;
    }

    private void HighlightSymbol(Button? selected)
    {
        foreach (Button button in SymbolButtons.Children)
        {
            button.Background = selected == button ? EditorModalDesign.Color("#EFF6FC") : Brushes.White;
            button.BorderBrush = EditorModalDesign.Color(selected == button ? "#005FB8" : "#D1D1D1");
        }
    }

    internal void InsertTemplate(MathTemplate template)
    {
        var start = Input.SelectionStart;
        var insertion = template.Insert(Input.SelectedText);
        Input.SelectedText = insertion.Text;
        Input.Focus();
        Input.Select(start + insertion.SelectionStart, insertion.SelectionLength);
    }
}
