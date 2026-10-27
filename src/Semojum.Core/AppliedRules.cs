using System.Text.Json;

namespace Semojum.Core;

public sealed record AppliedRuleRow(string Symbol, string Cells, string Section, string Title, string Text, string Source, string RuleId);
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
        var trail = local ?? block.Metadata.Rules ?? [];
        var offsets = DocumentNavigation.LineOffsets(block.Braille);
        var rows = new List<AppliedRuleRow>();
        foreach (var rule in trail)
        {
            if (rule.LineNo >= offsets.Length && local is null) continue;
            var a = rule.LineNo < 0 ? 0 : (local is null ? offsets[Math.Max(0, rule.LineNo)] : 0) + rule.ColumnStart;
            var b = rule.LineNo < 0 ? block.Braille.Length : (local is null ? offsets[Math.Max(0, rule.LineNo)] : 0) + rule.ColumnEnd;
            if (a < 0 || b < a || b > block.Braille.Length) continue;
            var selected = rule.LineNo < 0 || (length == 0 ? a <= start && (start < b || a == b && start == a) : a < start + length && b > start);
            if (!selected) continue;
            var label = rule.Tag switch { "number_sign" => "수표", "contraction" => "약자", "symbol" => "기호", "tn_open" or "tn_close" => "점역자 주",
                "blank_table" or "blank_rule" or "blank_box" => "빈칸", "emphasis_open" or "emphasis_close" => "강조", "line_wrap" => "줄바꿈", _ => rule.Title };
            rows.Add(new(label, block.Braille[a..b], rule.Section, rule.Title, rule.Excerpt, rule.Source, rule.RuleId));
        }
        return rows.Distinct().ToArray();
    }
}
