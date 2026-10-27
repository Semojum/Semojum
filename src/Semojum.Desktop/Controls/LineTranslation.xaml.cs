using System.Windows;
using System.Windows.Controls;

namespace Semojum.Desktop.Controls;

public partial class LineTranslation : UserControl
{
    public event EventHandler? CloseRequested;
    public LineTranslation() => InitializeComponent();
    public void SetPreview(int line, string cells)
    {
        LineLabel.Text = "줄번역"; LineLabel.ToolTip = line > 0 ? $"텍스트 {line}줄의 점역 미리보기" : "점역 미리보기";
        Cells.Text = cells;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
