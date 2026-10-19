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
    public static BrailleTag Get(string name) => All.FirstOrDefault(t => t.Name == name)
        ?? throw new ArgumentException("지원하지 않는 점역 태그입니다: " + name, nameof(name));
}
