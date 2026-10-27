namespace Semojum.Core;

/// <summary>Apply the book's initial choice to a newly uploaded result; never filter or delete drafts.</summary>
public static class VisualDraftSelection
{
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
        var selected = Array.FindIndex(drafts, d => d.Label.Trim() == choice);
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
