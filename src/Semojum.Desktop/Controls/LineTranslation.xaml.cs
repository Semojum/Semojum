using System.Windows;
using System.Windows.Controls;

namespace Semojum.Desktop.Controls;

public partial class LineTranslation : UserControl
{
    public event EventHandler? CloseRequested;
    public LineTranslation() => InitializeComponent();
    public void SetPreview(int line, string cells)
    {
        LineLabel.Text = line > 0 ? $"줄번역 · 텍스트 {line}줄" : "줄번역";
        Cells.Text = cells;
    }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
