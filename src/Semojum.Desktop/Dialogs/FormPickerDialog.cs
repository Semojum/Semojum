using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class FormPickerDialog : CompactDialog
{
    public BrailleForm? Selected => ChoicesList.SelectedItem as BrailleForm;
    internal ListBox ChoicesList { get; } = new();
    public FormPickerDialog(IEnumerable<BrailleForm> forms, BrailleForm? selected) : base("양식 고르기", 320, 192, popover: true)
    {
        var panel = new DockPanel(); Body(panel);
        var header = new DockPanel { Margin = new(11, 0, 3, 0) };
        var close = Button("✕", () => { Close(); return Task.CompletedTask; });
        close.Width = close.MinWidth = close.Height = close.MinHeight = 24; close.Margin = new(0); close.Padding = new(0);
        close.Background = close.BorderBrush = Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetName(close, "닫기");
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        // Figma 1:1447: semibold title text from y=7, #B5B5B5 rule at y=31, 24px rows from y=40 with the highlight spanning x=4..315.
        header.Children.Add(new TextBlock { Text = Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 6, 0, 0) });
        var title = new Border { Height = 31, Background = new SolidColorBrush(Color.FromRgb(251, 251, 251)), BorderBrush = new SolidColorBrush(Color.FromRgb(181, 181, 181)), BorderThickness = new(0, 0, 0, 1), Child = header };
        DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
        ChoicesList.ItemsSource = forms; ChoicesList.DisplayMemberPath = nameof(BrailleForm.Name); ChoicesList.SelectedItem = selected;
        ChoicesList.BorderThickness = new(0); ChoicesList.Margin = new(3, 8, 3, 6); ChoicesList.Padding = new(0);
        // The default ListBox template pads its border by 1px, which pushed six 24px rows past the viewport and showed a scrollbar.
        var surface = new ControlTemplate(typeof(ListBox));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.FocusableProperty, false);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        surface.VisualTree = scroll; ChoicesList.Template = surface;
        ChoicesList.ItemContainerStyle = (Style)FindResource("FormChoice"); panel.Children.Add(ChoicesList);
        void Accept() { if (Selected is not null) DialogResult = true; }
        ChoicesList.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(ChoicesList, e.OriginalSource as DependencyObject) is ListBoxItem item)
            { ChoicesList.SelectedItem = item.Content; Accept(); e.Handled = true; }
        };
        ChoicesList.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        Loaded += (_, _) => { ChoicesList.Focus(); if (Selected is not null) ChoicesList.ScrollIntoView(Selected); };
    }
    internal static void Attach(ComboBox picker, Window owner)
    {
        picker.DropDownOpened += (_, _) =>
        {
            picker.IsDropDownOpen = false;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = new FormPickerDialog(picker.Items.Cast<BrailleForm>(), picker.SelectedItem as BrailleForm) { Owner = owner };
                if (dialog.ShowDialog() == true) picker.SelectedItem = dialog.Selected;
            }));
        };
    }
}
