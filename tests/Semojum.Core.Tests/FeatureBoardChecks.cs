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
    }
}
