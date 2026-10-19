namespace Semojum.Core;

public static class VisualDescriptions
{
    public static readonly IReadOnlyDictionary<string, string[]> Choices = new Dictionary<string, string[]>
    {
        ["figure"] = ["생략", "짧은 제목", "개조식", "상세", "줄글", "별책 참조"],
        ["table"] = ["테두리+구분선", "테두리만", "행열 바꿈", "풀어쓰기"],
        ["chart"] = ["생략", "짧은 제목", "개조식", "상세", "줄글", "별책 참조"],
        ["diagram"] = ["생략", "짧은 제목", "개조식", "상세", "관행형", "상향식"],
        ["cartoon"] = ["생략", "개조식", "줄글", "별책 참조"]
    };
    public static Dictionary<string, VisualDescription> Defaults() => new()
    {
        ["figure"] = new(["짧은 제목", "개조식", "상세", "줄글"], "개조식"),
        ["table"] = new(["테두리+구분선", "테두리만", "행열 바꿈", "풀어쓰기"], "테두리+구분선"),
        ["chart"] = new(["짧은 제목", "개조식", "상세"], "개조식"),
        ["diagram"] = new(["짧은 제목", "개조식", "상세", "관행형"], "개조식"),
        ["cartoon"] = new(["개조식", "줄글"], "개조식")
    };
    public static void Validate(IReadOnlyDictionary<string, VisualDescription> values)
    {
        foreach (var (type, value) in values)
            if (!Choices.TryGetValue(type, out var choices) || value.Options.Length == 0
                || value.Options.Distinct().Count() != value.Options.Length || value.Options.Any(o => !choices.Contains(o))
                || !value.Options.Contains(value.DefaultOption)) throw new ArgumentException("유형별로 하나 이상의 설명과 그중 기본 설명을 골라 주세요.");
    }
}
