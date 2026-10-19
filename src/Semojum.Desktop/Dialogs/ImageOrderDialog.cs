using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Semojum.Desktop.Dialogs;

public sealed class ImageOrderDialog : CompactDialog
{
    public string[] Paths { get; private set; }
    public ImageOrderDialog(string[] paths) : base("이미지 올리기", 480, 440)
    {
        Paths = paths.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var order = new ObservableCollection<string>(Paths);
        var panel = new DockPanel { Margin = new(16) }; Body(panel);
        var note = new TextBlock { Text = $"이미지 {paths.Length}장을 {paths.Length}쪽으로 올립니다.\n한 장에 두 쪽이 있으면 미리 잘라 주세요.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var items = new ListBox { ItemsSource = order, Height = 190, SelectedIndex = 0 };
        var image = new Image { Height = 80, Margin = new(8) }; DockPanel.SetDock(image, Dock.Bottom); panel.Children.Add(image);
        panel.Children.Add(items);
        items.SelectionChanged += (_, _) =>
        {
            if (items.SelectedItem is not string path) return;
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelHeight = 80; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); image.Source = bitmap; }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException) { image.Source = null; Status.Text = "미리보기를 읽을 수 없습니다: " + Path.GetFileName(path); }
        };
        void Move(int delta) { var i = items.SelectedIndex; if (i < 0 || i + delta < 0 || i + delta >= order.Count) return; order.Move(i, i + delta); items.SelectedIndex = i + delta; }
        Action("↑", () => Move(-1)); Action("↓", () => Move(1));
        Action("올리기", () => { Paths = order.ToArray(); DialogResult = true; }, true);
        Action("취소", () => DialogResult = false);
    }
}
