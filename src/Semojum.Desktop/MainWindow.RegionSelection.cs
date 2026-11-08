using Semojum.Core;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private DocumentSession? regionOwner;
    private EditorBlock[] regionBlocks = [];
    private (int Source, int Braille, int Metadata)[] regionRevisions = [];

    internal async Task SelectOriginalRegionAsync(EditorBlock block)
    {
        var owner = session;
        ClearOriginalRegion();
        if (!owner.Blocks.Contains(block)) return;
        await NavigateToAsync(new(block, SearchPanel.Text, block.SourceCaret));
        if (session != owner || session.ActiveBlock != block || !owner.Blocks.Contains(block)) return;
        // Equal bounding boxes denote the same original region; overlapping neighbours do not.
        var box = block.Metadata.BoundingBox;
        regionBlocks = owner.Blocks.Where(b => b == block || b.Page == block.Page
            && box is { Length: 4 } && b.Metadata.BoundingBox is { Length: 4 } other
            && box.SequenceEqual(other)).ToArray();
        regionOwner = owner;
        regionRevisions = regionBlocks.Select(b => (b.SourceRevision, b.BrailleRevision, b.MetadataRevision)).ToArray();
        RefreshLinkedSource();
    }

    private void ClearOriginalRegion()
    {
        regionOwner = null; regionBlocks = []; regionRevisions = [];
        PageEditor.SetLinkedRegionBlocks([]);
        Braille.GridView.SetLinkedRanges([]); detachedPane?.GridView.SetLinkedRanges([]);
    }

    private bool RefreshOriginalRegion()
    {
        if (regionOwner is null) return false;
        if (regionOwner != session || !regionBlocks.Contains(session.ActiveBlock!)
            || regionBlocks.Where((b, i) => !session.Blocks.Contains(b)
                || regionRevisions[i] != (b.SourceRevision, b.BrailleRevision, b.MetadataRevision)).Any())
        { ClearOriginalRegion(); return false; }
        PageEditor.SetLinkedRegionBlocks(regionBlocks);
        // Region ownership is known even without a character/line translation map.
        // A grid being recomposed suppresses these ranges until GridRefreshed runs.
        var ranges = regionBlocks.Where(b => !b.Metadata.IsBlocked)
            .Select(b => new BlockSpan(b, 0, b.Braille.Length)).ToArray();
        Braille.GridView.SetLinkedRanges(ranges); detachedPane?.GridView.SetLinkedRanges(ranges);
        return true;
    }
}
