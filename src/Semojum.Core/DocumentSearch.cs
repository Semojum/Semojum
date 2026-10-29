namespace Semojum.Core;

public enum SearchPanel { Text, Braille }
public sealed record SearchMatch(EditorBlock Block, int Offset, int Length);

/// <summary>Literal, case-insensitive matches. Offsets use the editor's UTF-16 indexing.</summary>
public static class DocumentSearch
{
    public static string Content(EditorBlock block, SearchPanel panel) => panel == SearchPanel.Text ? block.Source : block.Braille;

    public static IReadOnlyList<SearchMatch> FindAll(IEnumerable<EditorBlock> blocks, SearchPanel panel, string query)
    {
        if (string.IsNullOrEmpty(query) || panel == SearchPanel.Braille && !IsBrailleText(query)) return [];
        var matches = new List<SearchMatch>();
        foreach (var block in blocks)
        {
            var text = Content(block, panel);
            for (var offset = 0; offset <= text.Length - query.Length;)
            {
                var next = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (next < 0) break;
                matches.Add(new(block, next, query.Length));
                offset = next + query.Length;
            }
        }
        return matches;
    }

    public static SearchMatch? Next(IReadOnlyList<EditorBlock> blocks, SearchPanel panel, string query, EditorBlock? start, int offset)
    {
        var matches = FindAll(blocks, panel, query);
        if (matches.Count == 0) return null;
        if (start is null) return matches[0];
        var startIndex = blocks.ToList().IndexOf(start);
        if (startIndex < 0) return matches[0];
        var current = matches.FirstOrDefault(m => m.Block == start && m.Offset >= offset);
        if (current is not null) return current;
        // Index the first hit per block once: scanning every match for each empty block
        // makes a wrapped search quadratic in long documents.
        var firstByBlock = new Dictionary<EditorBlock, SearchMatch>();
        foreach (var match in matches) firstByBlock.TryAdd(match.Block, match);
        for (var step = 1; step < blocks.Count; step++)
        {
            if (firstByBlock.TryGetValue(blocks[(startIndex + step) % blocks.Count], out var next)) return next;
        }
        return firstByBlock.GetValueOrDefault(start);
    }

    public static bool IsBrailleText(string text) => text.All(c => c is >= '\u2800' and <= '\u28ff' || c is ' ' or '\n' or '\r' or '\t');
}
