using System.Windows;
using System.Windows.Controls;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class TranslationOptionsDialog : CompactDialog
{
    private readonly CheckBox auto;
    private readonly OptionsEditor editor = new();
    public BrailleOptions Result { get; private set; }
    public bool AutoTranslate => auto.IsChecked == true;
    public TranslationOptionsDialog(bool enabled, BrailleOptions? options = null) : base("작업 점역 옵션", 620, 600)
    {
        Result = options ?? new(); editor.Set(Result);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(editor);
        auto = new() { Content = "텍스트 입력이 멈추면 자동으로 점역", IsChecked = enabled, Margin = new(0, 12, 0, 12) };
        panel.Children.Add(auto);
        panel.Children.Add(new TextBlock { Text = "직접 수정한 점자는 유지 여부를 확인합니다. 저장은 Ctrl+S를 누를 때만 합니다.", TextWrapping = TextWrapping.Wrap });
        Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Action("확인", () => { Result = editor.Value; DialogResult = true; }, true);
        Action("취소", () => DialogResult = false);
        Action("기본값 복원", () => editor.Set(new()));
    }
}
