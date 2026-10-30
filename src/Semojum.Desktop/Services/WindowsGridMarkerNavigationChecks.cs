using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsGridMarkerNavigationChecks
{
    private sealed class PendingEngine : ITranslationEngine
    {
        internal TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Started { get; private set; }
        public Task<string> TranslateAsync(string text, int blockId, CancellationToken cancellationToken) { Started = true; return Result.Task; }
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await VerifyReadOnlyAsync(check);
        await VerifyAsync(check, review: false, changeDocument: false);
        await VerifyAsync(check, review: true, changeDocument: false);
        await VerifyDeletedAsync(check);
        await VerifyEditedAgainAsync(check, "source");
        await VerifyEditedAgainAsync(check, "braille");
        await VerifyEditedAgainAsync(check, "metadata");
        await VerifyManualAsync(check, cancel: false);
        await VerifyManualAsync(check, cancel: true);
        await VerifyAsync(check, review: false, changeDocument: true);
    }
    private static (MainWindow Window, EditorBlock First, EditorBlock Target, BrailleGridFace Face) Fixture(bool manual)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true); window.Show();
        var first = window.Session.ActiveBlock!; first.Source = "가"; first.Braille = "⠫"; first.HasManualBraille = manual;
        var target = new EditorBlock { Id = 2, Page = 1, Source = "나", Braille = "⠉", Translation = new("⠉", [], [new("검토", 1, "unknown")], false, []) };
        target.Memos.Add(new("fixture", null, "BRAILLE", 0, "⠉", "점자 메모", DateTimeOffset.Now));
        window.Session.Add(target); window.Session.ActiveBlock = first;
        var revisions = (Dictionary<EditorBlock, int>)typeof(MainWindow).GetField("translatedRevisions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        revisions[first] = first.SourceRevision; revisions[target] = target.SourceRevision;
        first.Source = "가나";
        var face = new BrailleGridFace(1, [[new GridCell(target, 0, '⠉')]]);
        window.Braille.GridView.Blocks = [first, target]; window.Braille.GridView.ItemsSource = new[] { face }; window.Braille.GridView.CoordinatesCurrent = true;
        return (window, first, target, face);
    }
    private static void Engine(MainWindow window, ITranslationEngine engine)
    {
        typeof(MainWindow).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, engine);
        typeof(MainWindow).GetField("translator", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new TranslationCoordinator(engine));
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        if (!condition()) throw new TimeoutException("Marker navigation fixture did not reach its expected state.");
    }
    private static async Task VerifyReadOnlyAsync(Action<bool, string> check)
    {
        var (window, first, target, face) = Fixture(false); var engine = new PendingEngine(); Engine(window, engine);
        window.Session.IsReadOnly = true; window.Session.IsDirty = false;
        var source = first.Source; var braille = first.Braille; var translation = first.Translation; var metadata = first.Metadata;
        var shown = false; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window && d.MessageText == "점역 검토 필요") is { } notice)
            { shown = notice.DetailText.Contains("검토"); timer.Stop(); notice.Close(); }
        };
        try
        {
            timer.Start(); window.Braille.GridView.ActivateMarker(face, 0, 64);
            await Task.Delay(30); var attempted = engine.Started;
            engine.Result.TrySetResult("⠃⠉"); await Task.Delay(150);
            check(!attempted && shown && ReferenceEquals(window.Session.ActiveBlock, target) && first.Source == source && first.Braille == braille
                && first.Translation == translation && first.Metadata == metadata && !window.Session.IsDirty,
                $"A read-only review marker opens without translating or changing source, braille, review data or dirty state (attempted={attempted}, shown={shown}, target={ReferenceEquals(window.Session.ActiveBlock, target)}, source={first.Source == source}, braille={first.Braille == braille}, review={first.Translation == translation}, metadata={first.Metadata == metadata}, dirty={window.Session.IsDirty})");
        }
        finally { timer.Stop(); engine.Result.TrySetCanceled(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static async Task VerifyAsync(Action<bool, string> check, bool review, bool changeDocument)
    {
        var (window, first, target, face) = Fixture(false); var engine = new PendingEngine(); Engine(window, engine);
        try
        {
            check(window.Braille.GridView.ActivateMarker(face, 0, review ? 64 : 48), "The requested grid marker is actionable");
            await WaitAsync(() => engine.Started);
            check(ReferenceEquals(window.Session.ActiveBlock, first), "Marker navigation waits for the previous block's pending translation");
            if (changeDocument)
            {
                var timedOut = false;
                var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                watchdog.Tick += (_, _) =>
                {
                    timedOut = true; watchdog.Stop();
                    foreach (Window child in Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != window).ToArray())
                    { Console.WriteLine($"[marker-check] timeout window={child.GetType().Name} title={child.Title}"); child.Close(); }
                };
                watchdog.Start();
                try
                {
                    var newDocument = new DocumentSession(); newDocument.Add(new EditorBlock { Id = 10, Page = 1, Source = "새 문서" });
                    typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [newDocument, null, false, false]);
                    var current = window.Session;
                    engine.Result.SetResult("⠃⠉"); await Task.Delay(150);
                    check(!timedOut && ReferenceEquals(window.Session, current) && !current.Blocks.Contains(target)
                        && !Application.Current.Windows.OfType<MemoThreadDialog>().Any(d => d.IsVisible && d.Owner == window),
                        "Changing tabs while translation is pending never opens a stale marker in the new document");
                    return;
                }
                finally { watchdog.Stop(); }
            }
            var shown = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                if (review && Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window && d.MessageText == "점역 검토 필요") is { } notice)
                { shown = notice.DetailText.Contains("검토"); timer.Stop(); notice.Close(); }
                if (!review && Application.Current.Windows.OfType<MemoThreadDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window) is { } memo)
                { shown = true; timer.Stop(); memo.Close(); }
            };
            timer.Start(); engine.Result.SetResult("⠃⠉");
            try { await WaitAsync(() => shown); } finally { timer.Stop(); }
            check(shown && first.Braille == "⠃⠉" && first.Translation?.SourceText == "가나" && ReferenceEquals(window.Session.ActiveBlock, target),
                review ? "Review marker translates the previous edit before opening its target warning" : "Memo marker translates the previous edit before opening its target memo");
        }
        finally { engine.Result.TrySetCanceled(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static async Task VerifyDeletedAsync(Action<bool, string> check)
    {
        var (window, first, target, face) = Fixture(false); var engine = new PendingEngine(); Engine(window, engine);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            foreach (var notice in Application.Current.Windows.OfType<NoticeDialog>().Where(d => d.IsVisible && d.Owner == window).ToArray()) notice.Close();
            foreach (var memo in Application.Current.Windows.OfType<MemoThreadDialog>().Where(d => d.IsVisible && d.Owner == window).ToArray()) memo.Close();
        };
        try
        {
            timer.Start(); window.Braille.GridView.ActivateMarker(face, 0, 48); await WaitAsync(() => engine.Started);
            window.Session.Blocks.Remove(target); engine.Result.SetResult("⠃⠉");
            await WaitAsync(() => first.Translation?.SourceText == "가나");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!ReferenceEquals(window.Session.ActiveBlock, target) && window.Session.Blocks.Contains(window.Session.ActiveBlock!),
                "A target deleted during pending translation never becomes the active block");
        }
        finally { timer.Stop(); engine.Result.TrySetCanceled(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static async Task VerifyEditedAgainAsync(Action<bool, string> check, string changed)
    {
        var (window, first, target, face) = Fixture(false); var engine = new PendingEngine(); Engine(window, engine);
        var memoShown = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (Application.Current.Windows.OfType<MemoThreadDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window) is { } memo)
            { memoShown = true; memo.Close(); }
        };
        try
        {
            timer.Start(); window.Braille.GridView.ActivateMarker(face, 0, 48); await WaitAsync(() => engine.Started);
            if (changed == "source")
            {
                window.PageEditor.FollowActive(first.Source.Length); window.PageEditor.Editor.Document.Insert(first.Source.Length, "다");
                check(first.Source == "가나다", "A user can continue editing the first source block while translation is pending");
            }
            else if (changed == "braille") first.EditBraille("⠫⠁");
            else first.Metadata = first.Metadata with { HeadingLevel = 2 };
            var expectedSource = first.Source; var expectedBraille = first.Braille; var expectedMetadata = first.Metadata; var expectedManual = first.HasManualBraille;
            engine.Result.SetResult("⠃⠉"); await Task.Delay(150);
            check(!memoShown && ReferenceEquals(window.Session.ActiveBlock, first) && first.Source == expectedSource
                && first.Braille == expectedBraille && first.Metadata == expectedMetadata && first.HasManualBraille == expectedManual,
                $"A newer {changed} edit cancels marker navigation without losing user content");
        }
        finally { timer.Stop(); engine.Result.TrySetCanceled(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static async Task VerifyManualAsync(Action<bool, string> check, bool cancel)
    {
        var (window, first, target, face) = Fixture(true); var decisionSeen = false; var memoShown = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window) is { } decision)
            { decisionSeen = true; decision.ActionButtons[cancel ? 2 : 0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
            if (Application.Current.Windows.OfType<MemoThreadDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window) is { } memo)
            { memoShown = true; memo.Close(); }
        };
        try
        {
            timer.Start(); window.Braille.GridView.ActivateMarker(face, 0, 48); await Task.Delay(100);
            check(decisionSeen && first.Braille == "⠫" && first.HasManualBraille && (cancel ? !memoShown && ReferenceEquals(window.Session.ActiveBlock, first) : memoShown && ReferenceEquals(window.Session.ActiveBlock, target)),
                cancel ? "Cancelling marker navigation keeps the first block and its manual braille" : "Keeping manual braille preserves it before opening the target memo");
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
