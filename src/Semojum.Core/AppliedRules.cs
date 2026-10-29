using System.Text.Json;

namespace Semojum.Core;

public sealed record AppliedRuleRow(string Symbol, string Cells, string Section, string Title, string Text, string Source, string RuleId, bool IsBlockEvidence = false);
public static class AppliedRules
{
    public static RuleEvidence Read(JsonElement row)
    {
        string? Text(string key) => row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Number(string key, int fallback = 0) => row.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;
        return new(Text("rule_id") ?? "", Text("source") ?? "", Text("section") ?? "", Text("rule_name") ?? Text("title") ?? "",
            Text("contents") ?? Text("excerpt") ?? "", Number("line_no", -1), Number("col_start"), Number("col_end"), Text("tag"));
    }

    public static AppliedRuleRow[] Select(EditorBlock block, int start, int length)
    {
        // Edits invalidate engine coordinates. Never attribute rules to a new manual cell by accident.
        if (block.HasManualBraille || block.Translation?.SourceText is { } source && source != block.Source) return [];
        var local = block.Translation?.RuleTrail;
        var rows = new List<AppliedRuleRow>();
        foreach (var rule in local ?? [])
        {
            var a = rule.LineNo < 0 ? 0 : rule.ColumnStart;
            var b = rule.LineNo < 0 ? block.Braille.Length : rule.ColumnEnd;
            if (a < 0 || b < a || b > block.Braille.Length) continue;
            var selected = rule.LineNo < 0 || (length == 0 ? a <= start && (start < b || a == b && start == a) : a < start + length && b > start);
            if (!selected) continue;
            rows.Add(new(Label(rule), block.Braille[a..b], rule.Section, rule.Title, rule.Excerpt, rule.Source, rule.RuleId));
        }
        // Server and legacy evidence can use source-text coordinates. Preserve it as
        // evidence attached to the original block, never as an invented cell range.
        foreach (var rule in block.Metadata.Rules ?? [])
        {
            var origin = rule.Origin switch { RuleEvidenceOrigin.TextPanel => "원문", RuleEvidenceOrigin.BraillePanel => "점자", _ => "출처 미확인" };
            rows.Add(new($"{Label(rule)} ({origin})", "", rule.Section, rule.Title, rule.Excerpt, rule.Source, rule.RuleId, true));
        }
        return rows.Distinct().ToArray();
    }

    private static string Label(RuleEvidence rule) => rule.Tag switch
    {
        "number_sign" => "수표", "contraction" => "약자", "symbol" => "기호", "tn_open" or "tn_close" => "점역자 주",
        "blank_table" or "blank_rule" or "blank_box" => "빈칸", "emphasis_open" or "emphasis_close" => "강조", "line_wrap" => "줄바꿈", _ => rule.Title
    };
}
