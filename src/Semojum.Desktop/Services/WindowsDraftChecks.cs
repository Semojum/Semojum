using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsDraftChecks
{
    internal static Task RunAsync(MainWindow window, Action<bool, string> check)
    {
        var block = window.Session.ActiveBlock!;
        var metadata = block.Metadata; var translation = block.Translation; var locked = block.IsBrailleLocked;
        var dirty = window.Session.IsDirty; var readOnly = window.Session.IsReadOnly;
        DependencyObject? current = window.DraftIcon;
        while (current is not null && current is not Button) current = VisualTreeHelper.GetParent(current);
        var button = current as Button ?? throw new InvalidOperationException("Draft toolbar button not found");
        try
        {
            block.Metadata = new(Type: "text"); block.Translation = null; block.IsBrailleLocked = false;
            check(!button.IsEnabled, "The draft toolbar command is disabled when the current block has no drafts");
            var items = new[] { window.SourceEditor.ContextMenu, window.PageEditor.Editor.ContextMenu, window.Braille.Editor.ContextMenu }
                .Select(menu => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "대체 초안 보기"))).ToArray();
            check(items.All(item => !item.IsEnabled), "Draft commands in editor menus are disabled without drafts");
            block.Metadata = new(Type: "image", Alternatives: [new("기본", "설명", null, "⠁", null)]);
            check(button.IsEnabled && items.All(item => item.IsEnabled), "Draft commands become available when the selected block receives alternatives");
            block.IsBrailleLocked = true;
            check(!button.IsEnabled && items.All(item => !item.IsEnabled), "Blocked braille cannot apply an alternative draft");
            block.IsBrailleLocked = false; window.Session.IsReadOnly = true;
            check(!button.IsEnabled && items.All(item => !item.IsEnabled), "Read-only documents cannot apply an alternative draft");
            window.Session.IsReadOnly = false;
            block.Metadata = new(Type: "text");
            block.Translation = BrailleTranslation.Plain("⠁") with { Drafts = [new("table_grid", "표", "⠁", [])] };
            check(button.IsEnabled && items.All(item => item.IsEnabled), "Table translation drafts remain available");
        }
        finally
        {
            block.Metadata = metadata; block.Translation = translation; block.IsBrailleLocked = locked;
            window.Session.IsReadOnly = readOnly; window.Session.IsDirty = dirty;
        }
        return Task.CompletedTask;
    }
}
