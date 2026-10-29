using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsSearchSafetyChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true);
        try
        {
            window.Show();
            var block = window.Session.Blocks[0];
            block.Source = "원문은 그대로";
            block.Braille = "⠁⠀<!쪽바꿈>⠃ ⠁";
            window.Session.ActiveBlock = block;
            var source = block.Source;
            await window.ReplaceMatchesAsync("쪽바꿈", "⠉", SearchPanel.Braille, true);
            check(block.Braille == "⠁⠀<!쪽바꿈>⠃ ⠁" && block.Source == source,
                "Braille replace all cannot corrupt a layout marker or source text");
            window.Braille.Editor.Select(4, 3);
            await window.ReplaceMatchesAsync("쪽바꿈", "⠉", SearchPanel.Braille, false);
            check(block.Braille == "⠁⠀<!쪽바꿈>⠃ ⠁" && block.Source == source,
                "Single braille replace cannot modify a selected layout marker");
            await window.ReplaceMatchesAsync("⠀", "⠉", SearchPanel.Braille, true);
            check(block.Braille == "⠁⠉<!쪽바꿈>⠃ ⠁" && block.Source == source && block.HasManualBraille,
                "Replacing a braille blank preserves the marker and source, and marks a manual correction");
            window.Session.IsReadOnly = true;
            await window.ReplaceMatchesAsync("⠉", "⠿", SearchPanel.Braille, true);
            check(block.Braille == "⠁⠉<!쪽바꿈>⠃ ⠁" && block.Source == source,
                "The find dialog cannot replace text in a read-only document");
            await window.ReplaceMatchesAsync("원문", "변경", SearchPanel.Text, true);
            check(block.Braille == "⠁⠉<!쪽바꿈>⠃ ⠁" && block.Source == source,
                "Read-only source replacement preserves both source and manual braille");
            window.Session.IsReadOnly = false;
            var locked = new EditorBlock { Id = 10001, Page = block.Page + 1, Source = "잠긴 원문", Braille = "⠉", IsBrailleLocked = true };
            window.Session.Add(locked);
            await window.ReplaceMatchesAsync("⠉", "⠿", SearchPanel.Braille, true);
            check(locked.Braille == "⠉" && locked.Source == "잠긴 원문" && block.Braille == "⠁⠿<!쪽바꿈>⠃ ⠁",
                "Replace all preserves locked braille while correcting editable braille");
            var blocked = new EditorBlock { Id = 10002, Page = block.Page + 2, Source = "변환 대기 원문", Braille = "⠿", Metadata = new(IsBlocked: true), IsBrailleLocked = true };
            window.Session.Add(blocked);
            await window.ReplaceMatchesAsync("변환 대기", "고침", SearchPanel.Text, true);
            check(blocked.Source == "변환 대기 원문" && blocked.Braille == "⠿",
                "Source replace all respects blocked conversion pages");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
