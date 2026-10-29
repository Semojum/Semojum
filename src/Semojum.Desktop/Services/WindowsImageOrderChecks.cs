using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsImageOrderChecks
{
    internal static void Run(Window owner, Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "semojum-image-order-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(folder, $"{i}.png")).ToArray();
        try
        {
            for (var i = 0; i < paths.Length; i++)
            {
                var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
                    new byte[] { 0, 0, (byte)(30 + i * 80), 255 }, 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(paths[i]); encoder.Save(output);
            }
            var input = new[] { paths[2], paths[0], paths[1] };
            var dialog = new ImageOrderDialog(input) { Owner = owner };
            dialog.Loaded += (_, _) =>
            {
                try
                {
                    dialog.UpdateLayout();
                    var items = Descendants(dialog).OfType<ListBox>()
                        .Single(x => AutomationProperties.GetName(x) == "이미지 순서");
                    var preview = Descendants(dialog).OfType<Image>()
                        .Single(x => AutomationProperties.GetName(x) == "선택한 이미지 미리보기");
                    check(items.Items.Cast<string>().SequenceEqual(input),
                        "Image selection order is not sorted by filename");
                    check(preview.Source is BitmapSource, "First image preview loads on initial selection");
                    var first = preview.Source;
                    items.SelectedIndex = 1;
                    check(preview.Source is BitmapSource && !ReferenceEquals(first, preview.Source),
                        "Changing selection updates image preview");
                    var target = (ListBoxItem)items.ItemContainerGenerator.ContainerFromIndex(0);
                    var drag = new DataObject();
                    drag.SetData("Semojum.ImageOrder.List", items);
                    drag.SetData("Semojum.ImageOrder.Index", 1);
                    var ctor = typeof(DragEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                        null, [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)], null)!;
                    var drop = (DragEventArgs)ctor.Invoke([drag, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(1, 1)]);
                    drop.RoutedEvent = UIElement.DropEvent;
                    target.RaiseEvent(drop);
                    check(Equals(items.Items[0], input[1]) && items.SelectedIndex == 0,
                        "Dropping a list item changes order and keeps it selected");
                    var down = Descendants(dialog).OfType<Button>().Single(x => Equals(x.Content, "↓"));
                    down.Focus();
                    var presentation = PresentationSource.FromVisual(down)!;
                    down.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentation, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                    down.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentation, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                    check(Equals(items.Items[1], input[1]) && items.SelectedIndex == 1,
                        "Keyboard-accessible move control updates order and selection");
                    var up = Descendants(dialog).OfType<Button>().Single(x => Equals(x.Content, "↑"));
                    up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Descendants(dialog).OfType<Button>().Single(x => Equals(x.Content, "올리기"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch { dialog.Close(); throw; }
            };
            check(dialog.ShowDialog() == true && dialog.Paths.SequenceEqual([input[1], input[0], input[2]]),
                "Confirmed image paths preserve the selected upload order");
            var broken = Path.Combine(folder, "0.png"); File.WriteAllText(broken, "not an image");
            var invalidDialog = new ImageOrderDialog([broken, paths[0]]) { Owner = owner };
            invalidDialog.Loaded += (_, _) =>
            {
                var items = Descendants(invalidDialog).OfType<ListBox>()
                    .Single(x => AutomationProperties.GetName(x) == "이미지 순서");
                var preview = Descendants(invalidDialog).OfType<Image>()
                    .Single(x => AutomationProperties.GetName(x) == "선택한 이미지 미리보기");
                check(preview.Source is null && Descendants(invalidDialog).OfType<TextBlock>()
                    .Any(x => x.Text.Contains("미리보기를 읽을 수 없습니다", StringComparison.Ordinal)),
                    "Unreadable image reports preview failure without using the previous image");
                items.SelectedIndex = 1;
                check(preview.Source is BitmapSource && !Descendants(invalidDialog).OfType<TextBlock>()
                    .Any(x => x.IsVisible && x.Text.Contains("미리보기를 읽을 수 없습니다", StringComparison.Ordinal)),
                    "Selecting a readable image clears the preview error");
                Descendants(invalidDialog).OfType<Button>().Single(x => Equals(x.Content, "취소"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            check(invalidDialog.ShowDialog() == false && invalidDialog.Paths.SequenceEqual([broken, paths[0]]),
                "Cancel closes the dialog without confirming a new order");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
