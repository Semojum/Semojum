using System.Windows.Controls;
using System.Windows.Media;
namespace Semojum.Desktop.Controls;
public partial class OriginalPreview : UserControl
{
    public OriginalPreview() => InitializeComponent();
    public void SelectBlock(int id, int page)
    {
        var blocks = new[] { BlockOne, BlockTwo, BlockThree };
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i].Background = i == id - 1 ? new SolidColorBrush(Color.FromRgb(237, 245, 254)) : Brushes.Transparent;
            blocks[i].BorderBrush = i == id - 1 ? new SolidColorBrush(Color.FromRgb(79, 133, 188)) : Brushes.Transparent;
        }
        PageLabel.Text = $"{page} / 128쪽";
    }
}
