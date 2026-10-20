using Semojum.Core;

if (args.Contains("--sidecar-fixture")) return await IntegrationChecks.RunFixtureAsync();

var tests = new (string Name, Func<Task> Run)[]
{
    ("Navigation validates absolute, relative, missing and overflow page numbers", () => Sync(() =>
    {
        var first = new EditorBlock { Id = 1, Page = 12, Source = "abc" };
        var next = new EditorBlock { Id = 2, Page = 14, Source = "다음" };
        EditorBlock[] blocks = [first, next];
        Equal(next, DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 0, NavigationTarget.OriginalPage, "+2").Block);
        Equal(first, DocumentNavigation.Resolve(blocks, next, SearchPanel.Text, 0, NavigationTarget.OriginalPage, "12").Block);
        Throws<ArgumentException>(() => DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 0, NavigationTarget.OriginalPage, "13"));
        foreach (var invalid in new[] { "0", "-12", "+2147483647", "NaN", "1.2", "" })
            Throws<ArgumentException>(() => DocumentNavigation.Number(invalid, 12));
        Equal(15, DocumentNavigation.Number(" +3 ", 12));
    })),
    ("Navigation uses CRLF-safe UTF-16 line offsets and rejects stale memo anchors", () => Sync(() =>
    {
        var first = new EditorBlock { Id = 1, Page = 1, Source = "😀a\r\nb\rc\n", Braille = "⠁\n⠃" };
        var next = new EditorBlock { Id = 2, Page = 2, Source = "검토", Translation = new("", [], [new("?", 1, "unknown")], false, []) };
        first.Memos.Add(new("a", null, "TEXT", 1, "b", "메모", DateTimeOffset.Now));
        first.Memos.Add(new("b", null, "TEXT", 20, "삭제된 줄", "오래된 메모", DateTimeOffset.Now));
        first.Memos.Add(new("c", null, "BRAILLE", 1, "⠃", "점자 메모", DateTimeOffset.Now));
        EditorBlock[] blocks = [first, next];
        Equal("0,5,7,9", string.Join(",", DocumentNavigation.LineOffsets(first.Source)));
        Equal(5, DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 0, NavigationTarget.Line, "+1").Offset);
        Equal(9, DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 7, NavigationTarget.Line, "+1").Offset);
        Throws<ArgumentException>(() => DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 7, NavigationTarget.Line, "5"));
        Equal(5, DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 0, NavigationTarget.Memo, "1").Offset);
        Equal(1, DocumentNavigation.MarkedLocations(blocks, NavigationTarget.Memo, SearchPanel.Text).Count);
        Equal(2, DocumentNavigation.Resolve(blocks, first, SearchPanel.Braille, 0, NavigationTarget.Memo, "1").Offset);
        Equal(next, DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 0, NavigationTarget.Review, "+1").Block);
        Equal(0, DocumentNavigation.Resolve(blocks, first, SearchPanel.Text, 0, NavigationTarget.Review, "1").Offset);
    })),
    ("Export validates ranges and retains exact BRF page bytes including blanks", () => Sync(() =>
    {
        var brf = "a\r\n#a\r\n\f\r\n\fbc\r\n#c\r\n\f";
        Equal(3, DocumentExport.BrfPages(brf).Length);
        Equal("\r\n\fbc\r\n#c\r\n\f", DocumentExport.SelectBraille(brf, new(ExportScope.BraillePages, 2, 3)));
        Equal(brf, DocumentExport.SelectBraille(brf, new()));
        Throws<ArgumentException>(() => DocumentExport.SelectBraille(brf, new(ExportScope.BraillePages, 0, 2)));
        Throws<ArgumentException>(() => DocumentExport.SelectBraille(brf, new(ExportScope.BraillePages, 3, 2)));
        Throws<ArgumentException>(() => DocumentExport.SelectBraille(brf, new(ExportScope.BraillePages, 1, 4)));
        Throws<InvalidDataException>(() => DocumentExport.BrfPages("a\r\n"));
        Equal("⠁\r\n⠼⠃\r\n\f⠉\r\n\r\n\f", DocumentExport.BrailleText([["⠁", "⠼⠃"], ["⠉", ""]]));
    })),
    ("Original-page export retains numbering and rejects incomplete pages", () => Sync(() =>
    {
        var model = new DocumentSession { Layout = new(CoverPages: 1, SourcePageStart: 10) };
        for (var page = 1; page <= 4; page++)
        {
            model.Add(new EditorBlock { Id = page, Page = page, Source = "원문 " + page, Braille = "⠁" });
            model.PageStatuses[page] = page == 4 ? "FAILED" : "COMPLETED";
        }
        var snapshot = SmjFile.Snapshot(model).Manifest;
        Throws<InvalidOperationException>(() => DocumentExport.RequireCompletedPages(snapshot));
        var selected = DocumentExport.SelectOriginal(snapshot, new(ExportScope.OriginalPages, 2, 3));
        DocumentExport.RequireCompletedPages(selected);
        Equal(2, selected.Blocks.Length); Equal("원문 2", selected.Blocks[0].Source);
        Equal(0, selected.Layout.CoverPages); Equal(10, selected.Layout.SourcePageStart);
        Equal(11, DocumentExport.SelectOriginal(snapshot, new(ExportScope.OriginalPages, 3, 3)).Layout.SourcePageStart);
        Equal(4, snapshot.Blocks.Length); Equal(1, snapshot.Layout.CoverPages);
    })),
    ("Document search keeps literal offsets across blocks and wraps at the end", () => Sync(() =>
    {
        var first = new EditorBlock { Id = 1, Page = 1, Source = "😀Ab aaA", Braille = "⠁⠃" };
        var second = new EditorBlock { Id = 2, Page = 2, Source = "ab\nab", Braille = "⠿" };
        EditorBlock[] blocks = [first, second];
        var matches = DocumentSearch.FindAll(blocks, SearchPanel.Text, "AB");
        Equal(3, matches.Count); Equal(2, matches[0].Offset); Equal(3, matches[2].Offset);
        Equal(second, DocumentSearch.Next(blocks, SearchPanel.Text, "ab", first, 4)!.Block);
        Equal(3, DocumentSearch.Next(blocks, SearchPanel.Text, "ab", second, 2)!.Offset);
        Equal(first, DocumentSearch.Next(blocks, SearchPanel.Text, "ab", second, 5)!.Block);
        Equal<SearchMatch?>(null, DocumentSearch.Next(blocks, SearchPanel.Text, "missing", first, 0));
        Equal(0, DocumentSearch.FindAll(blocks, SearchPanel.Text, "").Count);
        Equal(1, DocumentSearch.FindAll(blocks, SearchPanel.Text, "aa").Count);
        Equal(0, DocumentSearch.FindAll(blocks, SearchPanel.Text, ".*").Count);
        Equal(second, DocumentSearch.FindAll(blocks, SearchPanel.Braille, "⠿").Single().Block);
    })),
    ("Braille replacement accepts cells and whitespace without allowing prose", () => Sync(() =>
    {
        True(DocumentSearch.IsBrailleText("⠁⣿⠀ \n\r\t")); True(DocumentSearch.IsBrailleText(""));
        True(!DocumentSearch.IsBrailleText("abc")); True(!DocumentSearch.IsBrailleText("점자"));
    })),
    ("Six-dot chord emits once after all releases", () => Sync(() =>
    {
        var input = new SixDotInput(); input.Press('F'); input.Press('J'); input.Press('F');
        Equal<string?>(null, input.Release('F')); Equal("⠉", input.Release('J')); Equal<string?>(null, input.Release('J'));
        foreach (var key in "FDSJKL") input.Press(key);
        foreach (var key in "FDSJK") Equal<string?>(null, input.Release(key));
        Equal("⠿", input.Release('L'));
    })),
    ("Six-dot focus reset and remapping", () => Sync(() =>
    {
        var input = new SixDotInput(); input.Press('F'); input.Reset(); Equal<string?>(null, input.Release('F'));
        input.Configure("A S D J K L"); True(!input.Press('F')); input.Press('a'); Equal("⠁", input.Release('A'));
        Throws<ArgumentException>(() => input.Configure("A A D J K L"));
    })),
    ("Manual edits stay intact with Keep and Cancel", async () =>
    {
        var engine = new DeferredEngine(); var coordinator = new TranslationCoordinator(engine);
        var block = Block(); block.EditBraille("⠿");
        Equal(TranslationOutcome.Kept, await coordinator.TranslateAsync(block, ManualEditDecision.Keep, TimeSpan.FromSeconds(1)));
        Equal(TranslationOutcome.Cancelled, await coordinator.TranslateAsync(block, ManualEditDecision.Cancel, TimeSpan.FromSeconds(1)));
        Equal(0, engine.Requests.Count); Equal("⠿", block.Braille); True(block.HasManualBraille);
    }),
    ("Late translation cannot overwrite newer source", async () =>
    {
        var engine = new DeferredEngine(); var coordinator = new TranslationCoordinator(engine); var block = Block();
        var pending = coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(2));
        block.Source = "바뀐 원문"; engine.Requests[0].SetResult("⠁");
        Equal(TranslationOutcome.Superseded, await pending); Equal("⠃", block.Braille);
    }),
    ("Late translation cannot overwrite manual braille", async () =>
    {
        var engine = new DeferredEngine(); var coordinator = new TranslationCoordinator(engine); var block = Block();
        var pending = coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(2));
        block.EditBraille("⠿"); engine.Requests[0].SetResult("⠁");
        Equal(TranslationOutcome.Superseded, await pending); Equal("⠿", block.Braille); True(block.HasManualBraille);
    }),
    ("Out-of-order requests apply only the newest result", async () =>
    {
        var engine = new DeferredEngine(); var coordinator = new TranslationCoordinator(engine); var block = Block();
        var first = coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(2));
        var second = coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(2));
        engine.Requests[1].SetResult("⠉"); Equal(TranslationOutcome.Applied, await second);
        engine.Requests[0].SetResult("⠁"); Equal(TranslationOutcome.Superseded, await first); Equal("⠉", block.Braille);
    }),
    ("Keep decision invalidates a pending overwrite", async () =>
    {
        var engine = new DeferredEngine(); var coordinator = new TranslationCoordinator(engine); var block = Block(); block.EditBraille("⠿");
        var pending = coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(2));
        Equal(TranslationOutcome.Kept, await coordinator.TranslateAsync(block, ManualEditDecision.Keep, TimeSpan.FromSeconds(2)));
        engine.Requests[0].SetResult("⠁"); Equal(TranslationOutcome.Superseded, await pending); Equal("⠿", block.Braille);
    }),
    ("Timeout locks braille; retry unlocks it", async () =>
    {
        var engine = new DeferredEngine(); var coordinator = new TranslationCoordinator(engine); var block = Block();
        Equal(TranslationOutcome.TimedOut, await coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromMilliseconds(20)));
        True(block.IsBrailleLocked); block.EditBraille("⠿"); Equal("⠃", block.Braille);
        block.Source = "잠금 중에도 원문 편집 가능";
        var retry = coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(2)); engine.Requests[1].SetResult("⠙");
        Equal(TranslationOutcome.Applied, await retry); True(!block.IsBrailleLocked); Equal("⠙", block.Braille);
        engine.Requests[0].TrySetResult("⠁"); Equal("⠙", block.Braille);
    }),
    ("Engine errors keep existing braille", async () =>
    {
        var engine = new DeferredEngine(); var coordinator = new TranslationCoordinator(engine); var block = Block();
        var pending = coordinator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(2));
        engine.Requests[0].SetException(new InvalidOperationException("engine failure"));
        Equal(TranslationOutcome.Failed, await pending); Equal("⠃", block.Braille); True(block.IsBrailleLocked);
    }),
    ("Document change version tracks save-relevant edits", () => Sync(() =>
    {
        var session = DocumentSession.Sample(); var before = session.ChangeVersion;
        session.ActiveBlock!.Memo = "저장할 메모"; True(session.IsDirty && session.ChangeVersion > before);
        before = session.ChangeVersion; session.ActiveBlock.State = "진행 상태"; Equal(before, session.ChangeVersion);
        session.Layout = new PageLayout(40); True(session.ChangeVersion > before);
    })),
    ("Layout preserves blank lines and validates bounds", () => Sync(() =>
    {
        var layout = new PageLayout(8); Equal("12345678|90||끝", string.Join("|", layout.Wrap("1234567890\r\n\r\n끝")));
        Throws<ArgumentException>(() => new PageLayout(0).Validate());
    })),
    ("Save round trip retains Unicode, layout, and corrections", async () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "semojum-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "sample.mock.smi"); var session = DocumentSession.Sample();
            session.ActiveBlock!.EditBraille("⠿⠿"); session.ActiveBlock.Memo = "한글 메모"; session.Layout = new PageLayout(40, 26);
            var frozen = WorkFile.Snapshot(session); session.ActiveBlock.Source = "저장 요청 후 변경";
            await WorkFile.SaveAsync(path, frozen); var loaded = await WorkFile.LoadAsync(path);
            Equal("⠿⠿", loaded.Blocks[0].Braille); True(loaded.Blocks[0].HasManualBraille); Equal("한글 메모", loaded.Blocks[0].Memo);
            Equal(frozen.Blocks[0].Source, loaded.Blocks[0].Source); Equal(40, loaded.Layout.Columns); True(!loaded.IsDirty);
            await WorkFile.SaveAsync(path, WorkFile.Snapshot(session)); Equal(1, Directory.GetFiles(folder).Length);
            Equal("저장 요청 후 변경", (await WorkFile.LoadAsync(path)).Blocks[0].Source);
        }
        finally { Directory.Delete(folder, true); }
    }),
    ("Foreign and malformed work files are rejected", async () =>
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{\"Format\":\"other\",\"Version\":1,\"Blocks\":[]}");
            try { await WorkFile.LoadAsync(path); throw new Exception("Expected rejection"); } catch (InvalidDataException) { }
            var snapshot = WorkFile.Snapshot(DocumentSession.Sample());
            await WorkFile.SaveAsync(path, snapshot with { Blocks = [snapshot.Blocks[0], snapshot.Blocks[0]] });
            try { await WorkFile.LoadAsync(path); throw new Exception("Expected rejection"); } catch (InvalidDataException) { }
        }
        finally { File.Delete(path); }
    })
};
var failures = 0;
var allTests = tests.Concat(IntegrationChecks.Tests()).Concat(RemainingChecks.Tests()).Concat(SyncChecks.Tests()).ToArray();
foreach (var (name, run) in allTests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}\n{ex}"); }
}
Console.WriteLine($"{allTests.Length - failures}/{allTests.Length} passed");
return failures == 0 ? 0 : 1;

static Task Sync(Action action) { action(); return Task.CompletedTask; }
static void True(bool value) { if (!value) throw new Exception("Expected true"); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static EditorBlock Block() => new() { Id = 1, Source = "원문", Braille = "⠃" };
sealed class DeferredEngine : ITranslationEngine
{
    public List<TaskCompletionSource<string>> Requests { get; } = [];
    public Task<string> TranslateAsync(string text, int blockId, CancellationToken cancellationToken)
    {
        // Deliberately ignores cancellation to model an uncooperative external engine.
        var request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); Requests.Add(request); return request.Task;
    }
}
