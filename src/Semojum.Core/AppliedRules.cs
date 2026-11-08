using System.Text.Json;

namespace Semojum.Core;

public sealed record AppliedRuleRow(string Symbol, string Cells, string Section, string Title, string Text, string Source, string RuleId, bool IsBlockEvidence = false);
public static class AppliedRules
{
    public const string BrailleAxis = "braille", PrintAxis = "print";
    public static RuleEvidence Read(JsonElement row)
    {
        string? Text(string key) => row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Number(string key, int fallback = 0) => row.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;
        return new(Text("rule_id") ?? "", Text("source") ?? "", Text("section") ?? "", Text("rule_name") ?? Text("title") ?? "",
            Text("contents") ?? Text("excerpt") ?? "", Number("line_no", -1), Number("col_start"), Number("col_end"), Text("tag"),
            Axis: Text("axis") is BrailleAxis or PrintAxis ? Text("axis") : null);
    }

    /// <summary>
    /// Rows for the applied-rules dialog. Each rule is read on its own axis (engine 3.4.44): engine trail columns count braille
    /// cells, AI server columns count source characters, so a server column is never laid on a 32-cell braille line.
    /// Older data without an axis is read by where it came from (engine trail = braille, server metadata = print).
    /// </summary>
    public static AppliedRuleRow[] Select(EditorBlock block, int start, int length)
    {
        // Edits invalidate engine coordinates. Never attribute rules to a new manual cell by accident.
        var sourceCurrent = block.Translation?.SourceText is not { } translated || translated == block.Source;
        var cellsCurrent = !block.HasManualBraille && sourceCurrent;
        // Source columns stay valid while the source is the one the server read, even after the braille was edited.
        var printCurrent = (block.Metadata.OriginalSource ?? block.Translation?.SourceText ?? block.Source) == block.Source;
        var evidence = (cellsCurrent ? block.Translation?.RuleTrail ?? [] : []).Select(r => (Rule: r, Axis: r.Axis ?? BrailleAxis))
            .Concat((block.Metadata.Rules ?? []).Select(r => (Rule: r, Axis: r.Axis ?? PrintAxis)))
            .Where(e => e.Axis == BrailleAxis ? cellsCurrent : printCurrent).ToArray();
        var printed = new List<(RuleEvidence Rule, string Text)>(); var cells = new List<(RuleEvidence Rule, int Start, int End)>();
        var rows = new List<AppliedRuleRow>();
        foreach (var (rule, axis) in evidence)
        {
            if (rule.LineNo < 0) rows.Add(Row("줄 전체", "—", rule));
            else if (Span(axis == PrintAxis ? block.Source : block.Braille, rule) is not { } span || axis == PrintAxis && span.Start == span.End)
                rows.Add(Row("위치 미확인", "", rule, true));
            else if (axis == PrintAxis) printed.Add((rule, block.Source[span.Start..span.End]));
            else cells.Add((rule, span.Start, span.End));
        }
        // The same rule reported on both axes (server text and braille, or server text and a local retranslation) is one place:
        // pair the k-th print hit with the k-th braille hit of that rule so the row shows the source character and its cells.
        foreach (var (rule, a, b) in cells)
        {
            var match = printed.FindIndex(p => p.Rule.RuleId == rule.RuleId);
            var symbol = match >= 0 ? printed[match].Text : Position(block.Braille, a, b);
            if (match >= 0) printed.RemoveAt(match);
            if (length == 0 ? a <= start && (start < b || a == b && start == a) : a < start + length && b > start) rows.Add(Row(symbol, block.Braille[a..b], rule));
        }
        // A source character without a cell range cannot be matched to the braille selection; list it with the block.
        rows.AddRange(printed.Select(p => Row(p.Text, "", p.Rule)));
        return rows.Distinct().ToArray();
    }

    private static AppliedRuleRow Row(string symbol, string cells, RuleEvidence rule, bool unplaced = false) =>
        new(symbol, cells, rule.Section, rule.Title, rule.Excerpt, rule.Source, rule.RuleId, unplaced);

    /// <summary>Absolute range of a rule in <paramref name="text"/>. Line 0 columns may run across lines (engine cells count the newline).</summary>
    private static (int Start, int End)? Span(string text, RuleEvidence rule)
    {
        var lineStart = 0;
        for (var line = 0; line < rule.LineNo; line++)
        {
            var next = text.IndexOf('\n', lineStart);
            if (next < 0) return null;
            lineStart = next + 1;
        }
        var lineEnd = text.IndexOf('\n', lineStart); if (lineEnd < 0) lineEnd = text.Length;
        int a = lineStart + rule.ColumnStart, b = lineStart + rule.ColumnEnd;
        return rule.ColumnStart < 0 || b < a || b > (rule.LineNo == 0 ? text.Length : lineEnd) ? null : (a, b);
    }

    /// <summary>Braille cell position (1-based line and cells) when the engine gives no source character for the range.</summary>
    private static string Position(string braille, int start, int end)
    {
        var lineStart = start == 0 ? 0 : braille.LastIndexOf('\n', start - 1) + 1;
        var line = braille.AsSpan(0, start).Count('\n') + 1; var column = start - lineStart + 1; var last = column + Math.Max(0, end - start - 1);
        return last == column ? $"{line}줄 {column}칸" : $"{line}줄 {column}~{last}칸";
    }
}
