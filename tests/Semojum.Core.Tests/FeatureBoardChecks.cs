internal static class FeatureBoardChecks
{
    private sealed class Source : IBoardListSource
    {
        internal bool Fail;
        internal bool Cancel;
        public Task<BoardPage> ListAsync(int page, string field, string keyword, bool mine, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("fixture failure");
            if (Cancel) return Task.FromCanceled<BoardPage>(new CancellationToken(true));
            var row = new BoardRow(page, keyword, 0, "fixture", false, "", 0);
            return Task.FromResult(new BoardPage(page, 21, [], [row]));
        }
    }

    internal static async Task RunAsync()
    {
        var source = new Source(); var model = new BoardListViewModel(source);
        var changed = new List<string?>(); model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await model.LoadAsync(2, true, "TITLE", "first");
        if (model.Page != 2 || !model.Mine || model.TotalPages != 3 || model.Rows.Single().Title != "first" || model.IsLoading
            || !changed.Contains(nameof(model.Rows)) || !changed.Contains(nameof(model.IsLoading))) throw new Exception("Board state did not publish a successful list.");
        var previousRows = model.Rows; source.Fail = true;
        try { await model.LoadAsync(1, false, "TITLE", "next"); throw new Exception("Expected list failure."); }
        catch (InvalidOperationException ex) when (ex.Message == "fixture failure") { }
        if (!ReferenceEquals(model.Rows, previousRows) || model.Page != 2 || !model.Mine || model.IsLoading) throw new Exception("Board state changed after a failed request.");
        source.Fail = false; source.Cancel = true;
        try { await model.LoadAsync(1, false, "TITLE", "next"); throw new Exception("Expected cancellation."); }
        catch (OperationCanceledException) { }
        if (!ReferenceEquals(model.Rows, previousRows) || model.Page != 2 || !model.Mine || model.IsLoading) throw new Exception("Board state changed after cancellation.");

        var lists = new ListsSource(); var board = new BoardListViewModel(lists);
        await board.LoadAsync(1, BoardListKind.Notices, "TITLE_CONTENT", "");
        if (board.Kind != BoardListKind.Notices || board.Mine || board.TotalCount != 2 || !board.Rows.All(r => r.Number == "공지")
            || !lists.Requests.SequenceEqual([(1, "", false)])) throw new Exception("The notice list did not show the notices of the unsearched first page.");
        await board.LoadAsync(1, BoardListKind.Notices, "AUTHOR", "관리");
        if (board.Rows.Single().Title != "기관 공지" || lists.Requests[^1] != (1, "", false)) throw new Exception("Searching notices did not filter the pinned notices locally.");
        lists.Requests.Clear();
        await board.LoadAsync(2, BoardListKind.Answered, "TITLE_CONTENT", "");
        if (board.Kind != BoardListKind.Answered || board.TotalCount != 13 || board.TotalPages != 2 || board.Rows.Count != 3 || board.Rows.Any(r => r.AnswerCount == 0)
            || !lists.Requests.SequenceEqual([(1, "", true), (2, "", true), (3, "", true)])) throw new Exception("Answered posts did not come from every page of my posts.");
    }

    // 25 of my posts over three pages, every second one answered; two pinned notices on the unsearched full list.
    private sealed class ListsSource : IBoardListSource
    {
        internal List<(int Page, string Keyword, bool Mine)> Requests { get; } = [];
        public Task<BoardPage> ListAsync(int page, string field, string keyword, bool mine, CancellationToken ct = default)
        {
            Requests.Add((page, keyword, mine));
            BoardRow[] notices = mine || keyword.Length > 0 ? [] : [new(0, "점검 안내", 0, "운영자", false, "", 0, "n1"), new(0, "기관 공지", 0, "기관 관리자", false, "", 0, "n2")];
            var items = Enumerable.Range(0, Math.Clamp(25 - (page - 1) * 10, 0, 10)).Select(i => 25 - (page - 1) * 10 - i)
                .Select(id => new BoardRow(id, $"글 {id}", id % 2, "fixture", false, "", 0)).ToArray();
            return Task.FromResult(new BoardPage(page, mine ? 25 : 40, notices, mine ? items : []));
        }
    }
}
