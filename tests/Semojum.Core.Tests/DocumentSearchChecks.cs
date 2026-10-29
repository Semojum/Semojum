using Semojum.Core;

internal static class DocumentSearchChecks
{
    public static Task RunAsync()
    {
        var block = new EditorBlock { Id = 1, Page = 1, Source = "원문 쪽바꿈", Braille = "⠁⠀<!쪽바꿈>⠃ ⠁" };
        EditorBlock[] blocks = [block];
        if (DocumentSearch.FindAll(blocks, SearchPanel.Braille, "쪽바꿈").Count != 0)
            throw new Exception("점자 검색이 내부 쪽바꿈 표식의 문자를 노출했습니다.");
        if (DocumentSearch.Next(blocks, SearchPanel.Braille, "쪽바꿈", block, 0) is not null)
            throw new Exception("다음 점자 검색이 내부 조판 표식으로 이동했습니다.");
        if (DocumentSearch.FindAll(blocks, SearchPanel.Braille, "⠀").Single().Offset != 1
            || DocumentSearch.FindAll(blocks, SearchPanel.Braille, "⠃ ⠁").Single().Offset != 8)
            throw new Exception("실제 점자 셀과 공백을 검색하지 못했습니다.");
        if (DocumentSearch.FindAll(blocks, SearchPanel.Text, "쪽바꿈").Single().Offset != 3)
            throw new Exception("원문 검색이 변경됐습니다.");
        var longDocument = Enumerable.Range(0, 8000).Select(i => new EditorBlock
            { Id = i + 1, Page = i + 1, Source = i < 4000 ? "찾을 내용" : "다른 내용" }).ToArray();
        var wrapped = DocumentSearch.Next(longDocument, SearchPanel.Text, "찾을", longDocument[4000], 0);
        if (wrapped?.Block != longDocument[0] || wrapped.Offset != 0)
            throw new Exception("긴 문서의 끝에서 다음 검색이 첫 결과로 돌아가지 않았습니다.");
        return Task.CompletedTask;
    }
}
