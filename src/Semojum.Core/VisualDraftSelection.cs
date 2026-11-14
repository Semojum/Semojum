namespace Semojum.Core;

/// <summary>Select and present description drafts without deleting original alternatives.</summary>
public static class VisualDraftSelection
{
    public static bool IsEnabled(BlockMetadata metadata, BrailleOptions options, AlternativeDraft draft, string? renderMode = null)
    {
        // Older documents without an explicit policy retain all their supplied drafts.
        if (options.VisualDescriptions is null) return true;
        var type = metadata.VisualSubtype is { } subtype && VisualDescriptions.Choices.ContainsKey(subtype) ? subtype : metadata.Type switch
        { "image" => "figure", "graph" or "chart_graph" => "chart", var other => other };
        if (!options.VisualDescriptions.TryGetValue(type, out var policy)) return true;
        if (type == "table" && renderMode is not null)
        {
            var label = renderMode switch { "table_grid" => "테두리+구분선", "linear" => "테두리만", "transposed" => "행열 바꿈", "unfold" => "풀어쓰기", _ => null };
            return label is null || policy.Options.Contains(label);
        }
        var name = draft.Label.Trim();
        if (VisualDescriptions.Choices.TryGetValue(type, out var choices) && choices.Contains(name))
            return policy.Options.Contains(name);
        // Current server names consolidate older detailed styles into option 2.
        return draft.Option switch
        {
            1 => policy.Options.Contains("생략"),
            6 => policy.Options.Contains("별책 참조"),
            7 => policy.Options.Contains("줄글"),
            2 => policy.Options.Any(o => o is "짧은 제목" or "개조식" or "상세"),
            _ when name is "설명" or "기본" => policy.Options.Any(o => o is "짧은 제목" or "개조식" or "상세" or "줄글"),
            _ => true // Preserve unknown server extensions rather than deleting inaccessible source data.
        };
    }
    public static bool ApplyInitial(EditorBlock block, BrailleOptions options)
    {
        if (block.HasManualBraille || block.Metadata.IsBlocked) return false;
        var metadata = block.Metadata;
        var type = metadata.VisualSubtype is { } subtype && VisualDescriptions.Choices.ContainsKey(subtype) ? subtype : metadata.Type switch
        { "image" => "figure", "graph" or "chart_graph" => "chart", var other => other };
        var policies = options.VisualDescriptions ?? VisualDescriptions.Defaults();
        if (!policies.TryGetValue(type, out var policy)) return false;
        var choice = policy.DefaultOption;
        if (metadata.Type == "table")
        {
            var mode = choice switch { "테두리+구분선" => "table_grid", "테두리만" => "linear", "행열 바꿈" => "transposed", "풀어쓰기" => "unfold", _ => null };
            if (mode is not null) block.Metadata = metadata = metadata with { RenderMode = mode };
        }
        var drafts = metadata.Alternatives ?? [];
        // BE 2026-11-01: drafts carry a fixed option number and labels have changed before, so the number pairs first.
        // Older pages have no number and keep the label rules below. Diagram-only names have no known number yet.
        int? option = choice switch { "생략" => 1, "별책 참조" => 6, "줄글" => 7, "짧은 제목" or "개조식" or "상세" => 2, _ => null };
        var selected = option is { } number ? Array.FindIndex(drafts, d => d.Option == number && IsEnabled(metadata, options, d)) : -1;
        if (selected < 0) selected = Array.FindIndex(drafts, d => d.Label.Trim() == choice);
        if (selected < 0 && choice == "생략") selected = Array.FindIndex(drafts, d => d.Label.Trim().EndsWith("생략", StringComparison.Ordinal));
        if (selected < 0 && choice == "별책 참조") selected = Array.FindIndex(drafts, d => d.Label.Contains("참조", StringComparison.Ordinal));
        // QA4: these legacy description styles were consolidated into the current "설명" / "기본" draft.
        if (selected < 0 && choice is "짧은 제목" or "개조식" or "상세" or "줄글")
            selected = Array.FindIndex(drafts, d => d.Label.Trim() is "설명" or "기본");
        if (selected < 0) return metadata.Type == "table" && metadata.RenderMode is not null;
        block.Metadata = metadata with { SelectedIndex = selected, OriginalSource = metadata.OriginalSource ?? block.Source };
        block.Source = drafts[selected].TaggedText ?? drafts[selected].Text;
        block.Translation = null;
        return true;
    }
}
