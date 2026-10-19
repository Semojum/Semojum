namespace Semojum.Core;

public enum SearchPanel { Text, Braille }
public sealed record SearchMatch(EditorBlock Block, int Offset, int Length);

/// <summary>Literal, case-insensitive matches. Offsets use the editor's UTF-16 indexing.</summary>
public static class DocumentSearch
{
    public static string Content(EditorBlock block, SearchPanel panel) => panel == SearchPanel.Text ? block.Source : block.Braille;

    public static IReadOnlyList<SearchMatch> FindAll(IEnumerable<EditorBlock> blocks, SearchPanel panel, string query)
    {
        if (string.IsNullOrEmpty(query)) return [];
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
        var startIndex = start is null ? -1 : blocks.ToList().IndexOf(start);
        if (startIndex < 0) return matches[0];
        var current = matches.FirstOrDefault(m => m.Block == start && m.Offset >= offset);
        if (current is not null) return current;
        for (var step = 1; step < blocks.Count; step++)
        {
            var next = matches.FirstOrDefault(m => m.Block == blocks[(startIndex + step) % blocks.Count]);
            if (next is not null) return next;
        }
        return matches.FirstOrDefault(m => m.Block == start);
    }

    public static bool IsBrailleText(string text) => text.All(c => c is >= '\u2800' and <= '\u28ff' || c is ' ' or '\n' or '\r' or '\t');
}
