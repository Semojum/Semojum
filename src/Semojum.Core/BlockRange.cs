namespace Semojum.Core;

public sealed record BlockSpan(EditorBlock Block, int Start, int Length);
public static class BlockRange
{
    public static string Text(IEnumerable<EditorBlock> blocks, SearchPanel panel) => string.Join("\n", blocks.Select(b => Value(b, panel)));
    public static string Value(EditorBlock block, SearchPanel panel) => panel == SearchPanel.Text ? block.Source : block.Braille;
    public static BlockSpan[] Select(IReadOnlyList<EditorBlock> blocks, SearchPanel panel, int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > Text(blocks, panel).Length) throw new ArgumentOutOfRangeException(nameof(start));
        var spans = new List<BlockSpan>(); var position = 0; var end = start + length;
        foreach (var b in blocks)
        {
            var value = Value(b, panel); var blockEnd = position + value.Length;
            if (start <= blockEnd && end >= position)
            {
                var from = Math.Clamp(start - position, 0, value.Length);
                var to = Math.Clamp(end - position, 0, value.Length);
                spans.Add(new(b, from, to - from));
            }
            position = blockEnd + 1;
        }
        return spans.ToArray();
    }
    public static EditorBlock Replace(DocumentSession document, IReadOnlyList<BlockSpan> spans, SearchPanel panel, string replacement, bool allowLayoutMarkers = false)
    {
        if (spans.Count == 0) throw new ArgumentException("선택한 구간이 없습니다.");
        if (document.IsReadOnly || spans.Any(s => s.Block.Metadata.IsBlocked || panel == SearchPanel.Braille && s.Block.IsBrailleLocked)) throw new ArgumentException("읽기 전용 또는 잠긴 블록은 수정할 수 없습니다.");
        if (spans.Any(s => !document.Blocks.Contains(s.Block) || s.Start < 0 || s.Length < 0 || (long)s.Start + s.Length > Value(s.Block, panel).Length)
            || spans.Select(s => s.Block).Distinct().Count() != spans.Count) throw new ArgumentException("선택한 내용이 바뀌었습니다. 다시 선택해 주세요.");
        if (panel == SearchPanel.Braille && !DocumentSearch.IsBrailleText(allowLayoutMarkers ? BrailleMarkup.VisibleText(replacement) : replacement)) throw new ArgumentException("점자와 공백만 붙여 넣을 수 있습니다.");
        var first = spans[0]; var last = spans[^1];
        if (panel == SearchPanel.Braille)
        {
            foreach (var span in spans) span.Block.EditBraille(span.Block.Braille.Remove(span.Start, span.Length).Insert(span.Start, span == first ? replacement : ""));
        }
        else
        {
            if (spans.Any(s => s.Block.Page != first.Block.Page)) throw new ArgumentException("같은 원본 쪽 안에서 선택해 주세요.");
            for (var i = 1; i < spans.Count; i++)
                if (document.Blocks.IndexOf(spans[i].Block) != document.Blocks.IndexOf(spans[i - 1].Block) + 1) throw new ArgumentException("연속한 블록을 선택해 주세요.");
            var value = first.Block.Source[..first.Start] + replacement + last.Block.Source[(last.Start + last.Length)..];
            // Merge before replacement to preserve original provenance, bounds and every memo.
            foreach (var span in spans.Skip(1)) BlockEditing.Merge(document, first.Block, span.Block, true);
            first.Block.Source = value; first.Block.Translation = null;
            for (var i = 0; i < first.Block.Memos.Count; i++)
            {
                var memo = first.Block.Memos[i]; var lines = (memo.Panel == "TEXT" ? value : first.Block.Braille).Count(c => c == '\n');
                first.Block.Memos[i] = memo with { LineIndex = Math.Clamp(memo.LineIndex, 0, lines) };
            }
            first.Block.State = "구간 편집 · 재점역 필요";
        }
        document.ActiveBlock = first.Block; return first.Block;
    }
}
