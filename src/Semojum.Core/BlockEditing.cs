namespace Semojum.Core;

public static class BlockEditing
{
    public static EditorBlock AddAfter(DocumentSession session, EditorBlock active)
    {
        var block = new EditorBlock { Id = session.Blocks.Max(b => b.Id) + 1, Page = active.Page, Heading = "새 블록" };
        session.Blocks.Insert(session.Blocks.IndexOf(active) + 1, block); session.ActiveBlock = block; return block;
    }
    public static EditorBlock Merge(DocumentSession session, EditorBlock first, EditorBlock second, bool keepBraille)
    {
        if (first.Page != second.Page || session.Blocks.IndexOf(second) != session.Blocks.IndexOf(first) + 1)
            throw new ArgumentException("같은 쪽의 인접 블록만 합칠 수 있습니다.");
        var sourceLines = first.Source.Count(c => c == '\n'); var brailleLines = first.Braille.Count(c => c == '\n');
        first.Source += second.Source; first.Braille += second.Braille;
        first.HasManualBraille = keepBraille && (first.HasManualBraille || second.HasManualBraille);
        first.Translation = null;
        first.Metadata = first.Metadata with { BoundingBox = Union(first.Metadata.BoundingBox, second.Metadata.BoundingBox),
            OriginalSource = (first.Metadata.OriginalSource ?? first.Source[..^second.Source.Length]) + (second.Metadata.OriginalSource ?? second.Source),
            Alternatives = null, Rules = (first.Metadata.Rules ?? []).Concat(second.Metadata.Rules ?? []).ToArray() };
        foreach (var memo in second.Memos) first.Memos.Add(memo with { LineIndex = memo.LineIndex + (memo.Panel == "TEXT" ? sourceLines : brailleLines) });
        session.Blocks.Remove(second); session.ActiveBlock = first;
        first.State = first.HasManualBraille ? "합친 블록 · 직접 수정 점자 유지" : "합친 블록 · 재점역 필요";
        return first;
    }
    public static void Delete(DocumentSession session, IReadOnlyCollection<EditorBlock> blocks)
    {
        if (blocks.Count == 0) return;
        var first = session.Blocks.IndexOf(blocks.First()); var page = blocks.First().Page;
        foreach (var b in blocks) session.Blocks.Remove(b);
        if (session.Blocks.All(b => b.Page != page)) session.Blocks.Insert(Math.Min(first, session.Blocks.Count), new EditorBlock { Id = session.Blocks.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1, Page = page, Heading = "빈 블록" });
        session.ActiveBlock = session.Blocks[Math.Clamp(first, 0, session.Blocks.Count - 1)];
    }
    private static double[]? Union(double[]? a, double[]? b) => a is not { Length: 4 } ? b : b is not { Length: 4 } ? a :
        [Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Max(a[2], b[2]), Math.Max(a[3], b[3])];
}
