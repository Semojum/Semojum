using System.Globalization;

namespace Semojum.Core;

public enum NavigationTarget { OriginalPage, Line, Review, Memo }
public sealed record NavigationLocation(EditorBlock Block, SearchPanel Panel, int Offset);

public static class DocumentNavigation
{
    public static int Number(string input, int current)
    {
        var text = input.Trim();
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException("번호 또는 +3, -3과 같은 상대 번호를 입력해 주세요.");
        var result = text.StartsWith('+') || text.StartsWith('-') ? (long)current + number : number;
        if (result < 1 || result > int.MaxValue) throw new ArgumentException("범위 안의 번호를 입력해 주세요.");
        return (int)result;
    }

    // Offsets use the editor's UTF-16 indices, including CRLF and supplementary characters.
    public static int[] LineOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; offsets.Add(i + 1); }
            else if (text[i] == '\n') offsets.Add(i + 1);
        }
        return offsets.ToArray();
    }

    public static NavigationLocation Resolve(IReadOnlyList<EditorBlock> blocks, EditorBlock active,
        SearchPanel panel, int caret, NavigationTarget target, string input)
    {
        if (!blocks.Contains(active)) throw new ArgumentException("현재 작업 블록을 찾을 수 없습니다.");
        if (target == NavigationTarget.OriginalPage)
        {
            var page = Number(input, active.Page);
            var block = blocks.FirstOrDefault(b => b.Page == page)
                ?? throw new ArgumentException("편집 가능한 원본 쪽 번호를 입력해 주세요. 실패한 쪽은 '쪽별 변환 상태'에서 확인할 수 있습니다.");
            return new(block, panel, 0);
        }
        if (target == NavigationTarget.Line)
        {
            var lines = LineOffsets(DocumentSearch.Content(active, panel));
            var current = Array.FindLastIndex(lines, offset => offset <= caret) + 1;
            var line = Number(input, current);
            if (line > lines.Length) throw new ArgumentException($"현재 블록의 1–{lines.Length}줄 범위로 입력해 주세요.");
            return new(active, panel, lines[line - 1]);
        }
        var locations = MarkedLocations(blocks, target, panel);
        if (locations.Count == 0) throw new ArgumentException(target == NavigationTarget.Memo ? "이동할 메모가 없습니다." : "검토가 필요한 점역 블록이 없습니다.");
        var activeIndex = blocks.ToList().IndexOf(active);
        var currentIndex = locations.FindLastIndex(l => blocks.ToList().IndexOf(l.Block) < activeIndex || l.Block == active && l.Offset <= caret);
        var number = Number(input, currentIndex + 1);
        if (number > locations.Count) throw new ArgumentException($"1–{locations.Count}번 범위로 입력해 주세요.");
        return locations[number - 1];
    }

    public static List<NavigationLocation> MarkedLocations(IReadOnlyList<EditorBlock> blocks, NavigationTarget target, SearchPanel panel)
    {
        var result = new List<NavigationLocation>();
        foreach (var block in blocks)
        {
            if (target == NavigationTarget.Review && block.Translation?.NeedsReview == true)
                result.Add(new(block, SearchPanel.Text, 0));
            if (target != NavigationTarget.Memo) continue;
            var lines = LineOffsets(DocumentSearch.Content(block, panel));
            foreach (var memo in block.Memos.Where(m => m.Panel == (panel == SearchPanel.Text ? "TEXT" : "BRAILLE"))
                .OrderBy(m => m.LineIndex).DistinctBy(m => m.LineIndex))
                if (memo.LineIndex >= 0 && memo.LineIndex < lines.Length) result.Add(new(block, panel, lines[memo.LineIndex]));
        }
        return result;
    }
}
