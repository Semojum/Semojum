using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private BrailleGridView? lastGrid;
    private DocumentSession? gridOwner;
    private long composedVersion = -1;
    private Task<ComposedBraille>? composing;
    private void ConnectGrid(BraillePane pane)
    {
        pane.GridView.GotKeyboardFocus += (_, _) => { SetActiveEditor(pane.Editor); lastGrid = pane.GridView; };
        pane.ComposeGridAsync = model =>
        {
            if (engine is not LocalBrailleEngine local) return Task.FromResult(new ComposedBraille("", []));
            if (gridOwner == model && composedVersion == model.ChangeVersion && composing is not null && !composing.IsFaulted) return composing;
            gridOwner = model; composedVersion = model.ChangeVersion;
            composing = local.ComposeAsync(SmjFile.Snapshot(model, false).Manifest, documentLifetime.Token);
            return composing;
        };
        pane.ReplaceGridAsync = (spans, text) =>
        {
            if (session.IsReadOnly || spans.Any(s => s.Block.IsBrailleLocked)) return Task.CompletedTask;
            if (spans.Any(s => s.Block.Page != spans[0].Block.Page)) throw new ArgumentException("같은 원본 쪽 안에서 선택해 주세요.");
            session.ActiveBlock = spans[0].Block;
            EditStructure(() => BlockRange.Replace(session, spans, SearchPanel.Braille, text));
            return Task.CompletedTask;
        };
        pane.GridView.MoveBlock = (moving, target) =>
        {
            if (moving == target || !session.Blocks.Contains(moving) || !session.Blocks.Contains(target)) return;
            if (moving.Page != target.Page) { session.Status = "블록은 같은 원본 쪽 안에서 이동해 주세요."; return; }
            if (session.IsReadOnly || moving.IsBrailleLocked || target.IsBrailleLocked) return;
            session.ActiveBlock = moving;
            EditStructure(() =>
            {
                var index = session.Blocks.IndexOf(target);
                if (session.Blocks.IndexOf(moving) < index) index--;
                session.Blocks.Move(session.Blocks.IndexOf(moving), index);
            });
        };
        pane.GridView.ReplayHistory = redo => ReplayHistory(redo);
    }
}
