using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Semojum.Desktop.Dialogs;

public sealed class SaveAsDialog : CompactDialog
{
    private readonly TextBox name;
    private readonly ComboBox format;
    public string FileName { get; private set; } = "";
    public int Format => format.SelectedIndex;
    public SaveAsDialog(string title, string? currentPath) : base("다른 이름으로 저장", 520, 190)
    {
        var body = new StackPanel { Margin = new(16, 12, 16, 0) }; Body(body);
        name = new TextBox { Text = Path.GetFileNameWithoutExtension(title) };
        format = Choices(["세모점 작업 파일 (*.smj)", "점자 파일 (*.brf)", "원문 텍스트 (*.txt)"]); format.SelectedIndex = 0;
        void Field(string label, Control input)
        {
            var row = new DockPanel { Height = 40 }; row.Children.Add(new TextBlock { Text = label, Width = 120, Foreground = (Brush)FindResource("Muted"), Margin = new(0, 4, 0, 0) });
            input.Height = input.MinHeight = 32; input.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(input); body.Children.Add(row);
            System.Windows.Automation.AutomationProperties.SetName(input, label);
        }
        Field("파일 이름", name); Field("파일 형식", format);
        Action("저장", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("파일 이름을 입력해 주세요.");
            var picker = new SaveFileDialog { Title = "저장할 위치 선택", FileName = name.Text.Trim(), AddExtension = true, OverwritePrompt = true,
                Filter = "세모점 작업 파일|*.smj|BRF 점자문서|*.brf|원문 텍스트|*.txt", FilterIndex = format.SelectedIndex + 1,
                DefaultExt = format.SelectedIndex switch { 1 => ".brf", 2 => ".txt", _ => ".smj" } };
            if (currentPath is not null) picker.InitialDirectory = Path.GetDirectoryName(currentPath);
            if (picker.ShowDialog(this) != true) return;
            FileName = picker.FileName; format.SelectedIndex = picker.FilterIndex - 1; DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
    }
}
