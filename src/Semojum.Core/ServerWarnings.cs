using System.Security.Cryptography;
using System.Text;

namespace Semojum.Core;

/// <summary>
/// Server quality issues (<c>critical_errors</c> and <c>review_flags</c>) shown alike as open warnings over their
/// whole block: the server names only the element, never a character range. Each issue becomes a text mark and a
/// braille mark whose ids derive from the block and the issue, so the same report never adds it twice. A person's
/// edit to either text resolves the block's warnings by making them stale; the stale marks stay in the .smj so the
/// same report cannot reopen them. Issues without an element stay in the page status only.
/// </summary>
public static class ServerWarnings
{
    private const string Prefix = "server-";
    private const string TextSuffix = "-text", BrailleSuffix = "-braille";

    /// <summary>Adds open warnings for every report of <paramref name="model"/> (opening a document with a server report).</summary>
    public static int Apply(DocumentSession model) =>
        model.QualityReports.Sum(report => Apply(model.Blocks.Where(b => b.Page == report.Key), report.Value));

    /// <summary>Adds the open warnings a page's report describes, skipping any already known, open or resolved.</summary>
    public static int Apply(IEnumerable<EditorBlock> blocks, IEnumerable<QualityIssue> issues)
    {
        var added = 0; var pageBlocks = blocks.Where(b => b.ServerId is not null).ToArray();
        var reported = issues.Where(i => i.ElementId is not null).ToArray();
        // BE 2026-11-01 review_flag "open" is the same unresolved warning; it adds one only when the report does not already name the block.
        // "sug" and "note" carry no text or range, so they stay in the block metadata only.
        var flagged = pageBlocks.Where(b => b.Metadata.ReviewFlag == "open" && !reported.Any(i => i.ElementId == b.ServerId))
            .Select(b => new QualityIssue("review", "review_flag", b.ServerId, "안 풀린 경고"));
        foreach (var issue in reported.Concat(flagged))
            foreach (var block in pageBlocks.Where(b => b.ServerId == issue.ElementId))
            {
                var key = Key(block.ServerId!, issue);
                string text = Id(key, ReviewPanel.Text), braille = Id(key, ReviewPanel.Braille);
                if (block.ReviewMarks.Any(m => m.Id == text || m.Id == braille)) continue;
                if (block.Source.Length > 0) block.ReviewMarks.Add(new(text, ReviewKind.OpenWarning, ReviewPanel.Text, 0, block.Source.Length, block.Source));
                if (block.Braille.Length > 0) block.ReviewMarks.Add(new(braille, ReviewKind.OpenWarning, ReviewPanel.Braille, 0, block.Braille.Length, block.Braille));
                if (block.Source.Length > 0 || block.Braille.Length > 0) added++;
            }
        return added;
    }

    /// <summary>A person changed the block: its open warnings are resolved and kept as stale records.</summary>
    internal static void Resolve(EditorBlock block)
    {
        for (var i = 0; i < block.ReviewMarks.Count; i++)
            if (block.ReviewMarks[i] is { Kind: ReviewKind.OpenWarning, IsStale: false } mark) block.ReviewMarks[i] = Resolved(mark);
    }

    internal static HashSet<string> OpenKeys(EditorBlock block) =>
        block.ReviewMarks.Where(m => m is { Kind: ReviewKind.OpenWarning, IsStale: false }).Select(m => KeyOf(m.Id)).OfType<string>().ToHashSet();

    /// <summary>After an automatic translation, warnings that were open cover the new cells in the braille panel.</summary>
    internal static void CoverBraille(EditorBlock block, IReadOnlySet<string> openKeys)
    {
        if (block.Braille.Length == 0) return;
        foreach (var key in openKeys)
        {
            var id = Id(key, ReviewPanel.Braille);
            var mark = new ReviewMark(id, ReviewKind.OpenWarning, ReviewPanel.Braille, 0, block.Braille.Length, block.Braille);
            var index = IndexOf(block, id);
            if (index < 0) block.ReviewMarks.Add(mark);
            else if (block.ReviewMarks[index] != mark) block.ReviewMarks[index] = mark;
        }
    }

    public static bool IsServerWarning(ReviewMark mark) => mark.Kind == ReviewKind.OpenWarning && KeyOf(mark.Id) is not null;
    /// <summary>Distinct server warnings (each has a text and a braille mark) that are open, or resolved.</summary>
    public static int Count(IEnumerable<ReviewMark> marks, bool resolved) =>
        marks.Where(m => IsServerWarning(m) && m.IsStale == resolved).Select(m => KeyOf(m.Id)).Distinct().Count();
    /// <summary>What a person's removal of a server warning leaves: the resolved record that stops it reappearing.</summary>
    public static ReviewMark Resolved(ReviewMark mark) => mark.IsStale ? mark : mark with { Start = 0, Length = 0, IsStale = true };

    private static int IndexOf(EditorBlock block, string id)
    {
        for (var i = 0; i < block.ReviewMarks.Count; i++) if (block.ReviewMarks[i].Id == id) return i;
        return -1;
    }
    // Block, severity, type and message identify the same warning; a short hash keeps the id compact in the .smj.
    private static string Key(string serverId, QualityIssue issue) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\u001f', serverId, issue.Severity, issue.Type, issue.Message))))[..16];
    private static string Id(string key, ReviewPanel panel) => Prefix + key + (panel == ReviewPanel.Text ? TextSuffix : BrailleSuffix);
    private static string? KeyOf(string id) =>
        !id.StartsWith(Prefix, StringComparison.Ordinal) ? null
        : id.EndsWith(TextSuffix, StringComparison.Ordinal) ? id[Prefix.Length..^TextSuffix.Length]
        : id.EndsWith(BrailleSuffix, StringComparison.Ordinal) ? id[Prefix.Length..^BrailleSuffix.Length] : null;
}
