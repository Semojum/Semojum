using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Semojum.Desktop.Dialogs;

public sealed class ImageOrderDialog : CompactDialog
{
    public string[] Paths { get; private set; }
    public ImageOrderDialog(string[] paths) : base("이미지 올리기", 720, 480)
    {
        Paths = paths.ToArray();
        var order = new ObservableCollection<string>(Paths);
        var panel = new DockPanel { Margin = new(16) }; Body(panel);
        var note = new TextBlock { Text = $"이미지 {paths.Length}장을 {paths.Length}쪽으로 올립니다.\n한 장에 두 쪽이 있으면 미리 잘라 주세요. 순서는 드래그하거나 Alt+↑/↓로 바꿀 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); panel.Children.Add(content);
        var items = new ListBox { ItemsSource = order, AllowDrop = true, Margin = new(0, 0, 8, 0) };
        AutomationProperties.SetName(items, "이미지 순서"); content.Children.Add(items);
        var image = new Image { Margin = new(8), Stretch = System.Windows.Media.Stretch.Uniform };
        AutomationProperties.SetName(image, "선택한 이미지 미리보기");
        Grid.SetColumn(image, 1); content.Children.Add(image);
        items.SelectionChanged += (_, _) =>
        {
            if (items.SelectedItem is not string path) return;
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1000; bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); image.Source = bitmap; Status.Text = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
            { image.Source = null; Status.Text = "미리보기를 읽을 수 없습니다: " + Path.GetFileName(path); }
        };
        items.SelectedIndex = order.Count > 0 ? 0 : -1;
        void MoveTo(int from, int to)
        {
            if (from < 0 || from >= order.Count || to < 0 || to >= order.Count || from == to) return;
            order.Move(from, to); items.SelectedIndex = to; items.ScrollIntoView(items.SelectedItem);
        }
        void Move(int delta) => MoveTo(items.SelectedIndex, items.SelectedIndex + delta);
        items.PreviewKeyDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is not (Key.Up or Key.Down)) return;
            Move(key == Key.Up ? -1 : 1); e.Handled = true;
        };
        Point dragStart = default; int dragIndex = -1;
        items.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(items);
            dragIndex = e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(items, origin) is ListBoxItem item
                ? items.ItemContainerGenerator.IndexFromContainer(item) : -1;
        };
        items.PreviewMouseMove += (_, e) =>
        {
            if (dragIndex < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(items);
            if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var data = new DataObject(); data.SetData("Semojum.ImageOrder.List", items); data.SetData("Semojum.ImageOrder.Index", dragIndex);
            dragIndex = -1; DragDrop.DoDragDrop(items, data, DragDropEffects.Move);
        };
        bool IsOwnDrag(DragEventArgs e) => e.Data.GetDataPresent("Semojum.ImageOrder.List")
            && ReferenceEquals(e.Data.GetData("Semojum.ImageOrder.List"), items)
            && e.Data.GetData("Semojum.ImageOrder.Index") is int;
        items.DragOver += (_, e) => { e.Effects = IsOwnDrag(e) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        items.Drop += (_, e) =>
        {
            if (!IsOwnDrag(e)) return;
            var from = (int)e.Data.GetData("Semojum.ImageOrder.Index")!;
            var target = e.OriginalSource is DependencyObject origin ? ItemsControl.ContainerFromElement(items, origin) as ListBoxItem : null;
            var insertion = target is null ? order.Count : items.ItemContainerGenerator.IndexFromContainer(target)
                + (e.GetPosition(target).Y >= target.ActualHeight / 2 ? 1 : 0);
            if (insertion > from) insertion--;
            MoveTo(from, Math.Clamp(insertion, 0, order.Count - 1)); e.Handled = true;
        };
        Action("↑", () => Move(-1)); Action("↓", () => Move(1));
        Action("올리기", () => { Paths = order.ToArray(); DialogResult = true; }, true);
        Action("취소", () => DialogResult = false);
    }
}
