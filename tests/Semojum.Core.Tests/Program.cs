using Semojum.Core;

var tests = new (string Name, Func<Task> Run)[]
{
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
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}\n{ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
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
