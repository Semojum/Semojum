using System.Text;

namespace Semojum.Feature.Editor;

public sealed record MathInsertion(string Text, int SelectionStart, int SelectionLength);

/// <summary>Guillemets mark editable defaults, never text saved in the document.</summary>
public sealed record MathTemplate(string Label, string Pattern)
{
    public MathInsertion Insert(string selectedText = "")
    {
        var text = new StringBuilder();
        int? selectionStart = null;
        var selectionLength = 0;
        var usedSelection = false;
        for (var i = 0; i < Pattern.Length; i++)
        {
            if (Pattern[i] != '«') { text.Append(Pattern[i]); continue; }
            var end = Pattern.IndexOf('»', i + 1);
            if (end < 0) throw new InvalidOperationException("Unclosed math template slot.");
            var value = Pattern[(i + 1)..end];
            if (!usedSelection && selectedText.Length > 0)
            {
                value = selectedText; usedSelection = true;
            }
            else if (selectionStart is null)
            {
                selectionStart = text.Length; selectionLength = value.Length;
            }
            text.Append(value); i = end;
        }
        return new(text.ToString(), selectionStart ?? text.Length, selectionLength);
    }
}

public sealed record MathTemplateGroup(string Label, string Icon, MathTemplate[] Templates);

public static class MathTemplates
{
    public static IReadOnlyList<MathTemplateGroup> Groups { get; } =
    [
        new("분수", "⅟", [
            new("분수", @"\frac{«a»}{«b»}"), new("사선 분수", @"{«a»}/{«b»}"),
            new("미분", @"\frac{d}{d«x»} «f(x)»"), new("편미분", @"\frac{\partial «f»}{\partial «x»}")]),
        new("첨자", "x²", [
            new("거듭제곱", @"{«x»}^{«2»}"), new("아래 첨자", @"{«x»}_{«i»}"),
            new("위아래 첨자", @"{«x»}_{«i»}^{«n»}"), new("지수 함수", @"e^{«x»}")]),
        new("근호", "√x", [
            new("제곱근", @"\sqrt{«x»}"), new("세제곱근", @"\sqrt[3]{«x»}"),
            new("n제곱근", @"\sqrt[«n»]{«x»}")]),
        new("적분", "∫", [
            new("부정적분", @"\int «f(x)»\,dx"), new("정적분", @"\int_{«a»}^{«b»} «f(x)»\,dx"),
            new("이중 적분", @"\int\!\int «f(x,y)»\,dx\,dy"), new("삼중 적분", @"\int\!\int\!\int «f(x,y,z)»\,dx\,dy\,dz"),
            new("폐곡선 적분", @"\oint «f(x)»\,dx")]),
        new("대형 연산자", "∑", [
            new("합", @"\sum_{«i=1»}^{«n»} «i»"), new("곱", @"\prod_{«i=1»}^{«n»} «i»"),
            new("큰 합집합", @"\bigcup_{«i=1»}^{«n»} «A_i»"), new("큰 교집합", @"\bigcap_{«i=1»}^{«n»} «A_i»")]),
        new("괄호", "(x)", [
            new("소괄호", @"\left(«x»\right)"), new("대괄호", @"\left[«x»\right]"),
            new("중괄호", @"\left\{«x»\right\}"), new("절댓값", @"\left|«x»\right|"),
            new("노름", @"\left\|«x»\right\|"),
            new("경우 나누기", @"\cases{«x» & «x\geq 0»\\«-x» & «x<0»}")]),
        new("함수", "sin", [
            new("사인", @"\sin{«x»}"), new("코사인", @"\cos{«x»}"), new("탄젠트", @"\tan{«x»}"),
            new("코시컨트", @"\csc{«x»}"), new("시컨트", @"\sec{«x»}"), new("코탄젠트", @"\cot{«x»}"),
            new("역사인", @"\arcsin{«x»}"), new("역코사인", @"\arccos{«x»}"), new("역탄젠트", @"\arctan{«x»}")]),
        new("악센트", "â", [
            new("벡터", @"\vec{«v»}"), new("모자", @"\hat{«x»}"), new("윗줄", @"\overline{«AB»}"),
            new("아랫줄", @"\underline{«x»}"), new("점 악센트", @"\dot{«x»}"), new("두 점 악센트", @"\ddot{«x»}"),
            new("물결 악센트", @"\tilde{«x»}")]),
        new("극한·로그", "lim", [
            new("극한", @"\lim_{«x\to 0»} «f(x)»"), new("무한대 극한", @"\lim_{«n\to\infty»} «a_n»"),
            new("로그", @"\log_{«2»}{«x»}"), new("자연로그", @"\ln{«x»}"),
            new("최댓값", @"\max_{«x»} «f(x)»"), new("최솟값", @"\min_{«x»} «f(x)»")]),
        new("연산·관계", "±", [
            new("플러스마이너스", @"\pm "), new("마이너스플러스", @"\mp "), new("곱하기", @"\times "), new("나누기", @"\div "),
            new("작거나 같음", @"\leq "), new("크거나 같음", @"\geq "), new("같지 않음", @"\neq "),
            new("근삿값", @"\approx "), new("항등", @"\equiv "), new("비례", @"\propto "),
            new("원소", @"\in "), new("원소가 아님", @"\not\in "), new("부분집합", @"\subset "),
            new("합집합", @"\cup "), new("교집합", @"\cap "), new("공집합", @"\emptyset "),
            new("오른쪽 화살표", @"\to "), new("양방향 화살표", @"\leftrightarrow "), new("함의", @"\Rightarrow "), new("동치", @"\Leftrightarrow ")]),
        new("행렬", "▦", [
            new("2×2 소괄호 행렬", @"\begin{pmatrix}«a»&«b»\\«c»&«d»\end{pmatrix}"),
            new("2×2 대괄호 행렬", @"\left[\matrix{«a»&«b»\\«c»&«d»}\right]"),
            new("3×3 행렬", @"\begin{pmatrix}«a»&«b»&«c»\\«d»&«e»&«f»\\«g»&«h»&«i»\end{pmatrix}"),
            new("행벡터", @"\begin{pmatrix}«a»&«b»&«c»\end{pmatrix}"),
            new("열벡터", @"\begin{pmatrix}«a»\\«b»\\«c»\end{pmatrix}"),
            new("행렬식", @"\left|\matrix{«a»&«b»\\«c»&«d»}\right|")]),
        new("기호", "αβ", [
            new("알파", @"\alpha "), new("베타", @"\beta "), new("감마", @"\gamma "), new("델타", @"\delta "),
            new("세타", @"\theta "), new("람다", @"\lambda "), new("뮤", @"\mu "), new("파이", @"\pi "),
            new("시그마", @"\sigma "), new("오메가", @"\omega "), new("대문자 델타", @"\Delta "), new("무한대", @"\infty "),
            new("각", @"\angle "), new("도", @"{«90»}^{\circ}"), new("줄임표", @"\cdots "),
            new("제곱미터", @"\mathrm{m}^{2}"), new("화학식", @"\mathrm{H}_{2}\mathrm{O}"), new("수식 안 텍스트", @"\text{«텍스트»}")])
    ];
}
