namespace Semojum.Core;

public sealed record BrailleTag(string Name, bool Paired)
{
    public string Open => $"<!{Name}>";
    public string Close => Paired ? $"<!/{Name}>" : "";
    public string Insert(string selection) => Paired ? Open + selection + Close : Open + selection;
}

public static class BrailleTags
{
    public static IReadOnlyList<BrailleTag> All { get; } =
    [
        new("쪽바꿈", false), new("장바꿈", false), new("머리말", true),
        new("왼쪽정렬", false), new("가운데정렬", false), new("오른쪽정렬", false),
        new("상단줄", false), new("중단줄", false), new("하단줄", false), new("상단굵은줄", false), new("하단굵은줄", false),
        new("표시작", false), new("표중앙", false), new("표끝", false), new("표구분선", false),
        new("표지시작", false), new("표지세로", false), new("표지끝", false),
        new("강조", true), new("굵은", true), new("색강조", true), new("취소선", true),
        new("밑줄빈칸", false), new("네모빈칸", false), new("주", true),
        new("묵자쪽번호", false), new("점자면번호", false), new("수식", true),
        new("상자", true), new("상자끝", false), new("상자2", false), new("상자3", false),
        new("네모글", true), new("기울임", true)
    ];
    // These structural tags are produced by proofreading. Keep parsing existing files, but do not offer manual insertion.
    public static IReadOnlyList<BrailleTag> Menu { get; } = All.Where(t => t.Name is not ("상자" or "상자끝" or "상자2" or "상자3" or "네모글" or "기울임")).ToArray();
    public static BrailleTag Get(string name) => All.FirstOrDefault(t => t.Name == name)
        ?? throw new ArgumentException("지원하지 않는 점역 태그입니다: " + name, nameof(name));

    /// <summary>
    /// A header tag carries its alignment as a leading alignment tag inside it (<c>&lt;!머리말&gt;&lt;!오른쪽정렬&gt;…</c>), which the
    /// engine removes from the text. Center writes no alignment tag, so it stays the older form that every reader takes as center.
    /// </summary>
    public static string InsertHeader(string text, string alignment) => Get("머리말").Insert(alignment switch
    {
        "left" => "<!왼쪽정렬>", "right" => "<!오른쪽정렬>", "center" => "",
        _ => throw new ArgumentException("머리말 정렬은 왼쪽·가운데·오른쪽 중 하나입니다.", nameof(alignment))
    } + text);

    /// <summary>The text and alignment (left, center or right) inside a header tag. A header without an alignment tag is centered.</summary>
    public static (string Text, string Alignment) ReadHeader(string inner)
    {
        var tag = System.Text.RegularExpressions.Regex.Match(inner, "^<!(왼쪽|가운데|오른쪽)정렬>");
        return !tag.Success ? (inner, "center")
            : (inner[tag.Length..], tag.Groups[1].Value switch { "왼쪽" => "left", "오른쪽" => "right", _ => "center" });
    }
}
