using System.Text.RegularExpressions;
namespace Semojum.Core;

/// <summary>Composition tokens are generated fields; grid selection copies only editable braille.</summary>
public static partial class BrailleMarkup
{
    [GeneratedRegex(@"<!(?:쪽바꿈|장바꿈|상단줄|중단줄|하단줄|상단굵은줄|하단굵은줄|표시작|표중앙|표끝|표구분선|표지시작|표지세로|표지끝|묵자쪽번호|점자면번호)>")]
    private static partial Regex Tokens();
    public static string VisibleText(string value) => Tokens().Replace(value, "");
    public static string Markers(string value) => string.Join("\n", Tokens().Matches(value).Select(m => m.Value));
    public static int MovePastMarker(string value, int offset, int direction)
    {
        foreach (Match m in Tokens().Matches(value))
            if (offset > m.Index && offset < m.Index + m.Length)
                return direction < 0 ? m.Index : m.Index + m.Length;
        return Math.Clamp(offset, 0, value.Length);
    }
}
