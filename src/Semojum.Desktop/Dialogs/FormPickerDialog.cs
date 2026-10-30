using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core.Integration;
using SharpVectors.Converters;
using static Semojum.Desktop.Dialogs.ExportRangeDialog;

namespace Semojum.Desktop.Dialogs;

public sealed class FormPickerDialog : CompactDialog
{
    public BrailleForm? Selected => ChoicesList.SelectedItem as BrailleForm;
    internal ListBox ChoicesList { get; } = new();
    internal BrailleForm? EditRequest { get; private set; }
    private readonly bool canEdit;
    public FormPickerDialog(IEnumerable<BrailleForm> forms, BrailleForm? selected, bool canEdit = false) : base("양식 고르기", 380, 208, popover: true)
    {
        this.canEdit = canEdit;
        var available = forms.ToArray(); Height = 32 + 16 + 32 * Math.Clamp(available.Length, 1, 6);
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = Brush("#5F5F5F"); frame.Background = Brushes.White; }
        var panel = new DockPanel(); Body(panel);
        var header = new DockPanel { Margin = new(11, 0, 3, 0) };
        var close = Button("닫기", () => { Close(); return Task.CompletedTask; });
        close.Width = close.MinWidth = close.Height = close.MinHeight = 24; close.Margin = new(0); close.Padding = new(0);
        close.Background = close.BorderBrush = Brushes.Transparent; close.Template = SurfaceTemplate(0);
        close.Content = DesignIcon("form-picker-close.svg", 16, 16); AutomationProperties.SetName(close, "닫기");
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = Title, FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 6, 0, 0) });
        var title = new Border { Height = 32, Background = Brush("#FBFBFB"), BorderBrush = Brush("#EAEAEA"), BorderThickness = new(0, 0, 0, 1), Child = header };
        DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title);
        ChoicesList.ItemsSource = available; ChoicesList.SelectedItem = selected;
        ChoicesList.BorderThickness = new(0); ChoicesList.Margin = new(3, 8, 3, 6); ChoicesList.Padding = new(0);
        StripListPadding(ChoicesList);
        ChoicesList.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(FormPickerRow)) };
        var item = new Style(typeof(ListBoxItem)); item.Setters.Add(new Setter(HeightProperty, 32d)); item.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        item.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch); border.AppendChild(presenter);
        item.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        var chosen = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; chosen.Setters.Add(new Setter(BackgroundProperty, Brush("#EFF6FC"))); item.Triggers.Add(chosen);
        // Bind the background so selected and unselected states remain visible.
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        item.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent)); ChoicesList.ItemContainerStyle = item; panel.Children.Add(ChoicesList);
        void Accept() { if (Selected is not null) DialogResult = true; }
        ChoicesList.PreviewMouseLeftButtonUp += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source is not null && source != ChoicesList; source = VisualTreeHelper.GetParent(source))
                if (source is Button) return;
            if (ItemsControl.ContainerFromElement(ChoicesList, e.OriginalSource as DependencyObject) is ListBoxItem choice)
            { ChoicesList.SelectedItem = choice.Content; Accept(); e.Handled = true; }
        };
        ChoicesList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            for (var source = e.OriginalSource as DependencyObject; source is not null && source != ChoicesList; source = VisualTreeHelper.GetParent(source))
                if (source is System.Windows.Controls.Button button)
                {
                    if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent, button));
                    e.Handled = true; return;
                }
            Accept(); e.Handled = true;
        };
        Loaded += (_, _) => { ChoicesList.Focus(); if (Selected is not null) ChoicesList.ScrollIntoView(Selected); };
    }
    internal void Edit(BrailleForm form)
    {
        if (!canEdit || form.FormId == "user:new") return;
        EditRequest = form; DialogResult = false;
    }
    internal bool CanEdit => canEdit;
    internal static void Attach(ComboBox picker, Window owner, Func<BrailleForm, Task>? edit = null)
    {
        picker.DropDownOpened += (_, _) =>
        {
            picker.IsDropDownOpen = false;
            owner.Dispatcher.BeginInvoke(new Action(async () =>
            {
                var dialog = new FormPickerDialog(picker.Items.Cast<BrailleForm>(), picker.SelectedItem as BrailleForm, edit is not null) { Owner = owner };
                if (dialog.ShowDialog() == true) picker.SelectedItem = dialog.Selected;
                else if (dialog.EditRequest is { } requested && edit is not null) { picker.SelectedItem = requested; await edit(requested); }
            }));
        };
    }
}

public sealed class FormPickerRow : Grid
{
    public FormPickerRow()
    {
        ColumnDefinitions.Add(new()); ColumnDefinitions.Add(new() { Width = new(32) }); ColumnDefinitions.Add(new() { Width = new(32) });
        var label = new TextBlock { Margin = new(16, 0, 0, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(BrailleForm.Name))); Children.Add(label);
        var edit = new Button { Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            Template = SurfaceTemplate(0), Content = DesignIcon("form-picker-edit.svg", 16, 16), ToolTip = "이 양식 편집" };
        Grid.SetColumn(edit, 1); Children.Add(edit);
        Loaded += (_, _) => { edit.IsEnabled = Window.GetWindow(this) is FormPickerDialog { CanEdit: true } && DataContext is BrailleForm { FormId: not "user:new" }; };
        DataContextChanged += (_, _) => { if (DataContext is BrailleForm f) AutomationProperties.SetName(edit, $"{f.Name} 편집"); };
        edit.Click += (_, e) => { e.Handled = true; if (DataContext is BrailleForm f && Window.GetWindow(this) is FormPickerDialog owner) owner.Edit(f); };
    }
}
