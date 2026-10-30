namespace Semojum.Core;

public enum ManualEditDecision { Keep, Overwrite, Cancel }
public enum TranslationOutcome { Applied, Kept, Cancelled, Superseded, TimedOut, Failed }
public interface ITranslationEngine
{
    Task<string> TranslateAsync(string text, int blockId, CancellationToken cancellationToken);
    async Task<BrailleTranslation> TranslateBlockAsync(EditorBlock block, CancellationToken cancellationToken) =>
        BrailleTranslation.Plain(await TranslateAsync(block.Source, block.Id, cancellationToken));
}

public sealed class MockTranslationEngine : ITranslationEngine
{
    public bool SimulateTimeout { get; set; }
    public async Task<string> TranslateAsync(string text, int blockId, CancellationToken cancellationToken)
    {
        await Task.Delay(SimulateTimeout ? 1500 : 90, cancellationToken);
        return SampleData.Braille[(Math.Max(1, blockId) - 1) % SampleData.Braille.Length];
    }
}

public sealed class TranslationCoordinator(ITranslationEngine engine)
{
    private readonly Dictionary<EditorBlock, long> generations = [];
    public void Invalidate(EditorBlock block) => generations[block] = generations.GetValueOrDefault(block) + 1;

    public async Task<TranslationOutcome> TranslateAsync(EditorBlock block, ManualEditDecision decision,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Even a Keep/Cancel decision invalidates a previous pending request.
        var generation = generations.GetValueOrDefault(block) + 1;
        generations[block] = generation;
        if (decision == ManualEditDecision.Cancel) return TranslationOutcome.Cancelled;
        if (block.HasManualBraille && decision == ManualEditDecision.Keep)
        {
            block.State = "직접 수정 유지";
            return TranslationOutcome.Kept;
        }
        var sourceRevision = block.SourceRevision;
        var brailleRevision = block.BrailleRevision;
        var metadataRevision = block.MetadataRevision;
        block.State = "점역 중…";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool IsCurrent() => generations[block] == generation && block.SourceRevision == sourceRevision
            && block.BrailleRevision == brailleRevision && block.MetadataRevision == metadataRevision;
        try
        {
            var pending = engine.TranslateBlockAsync(block, linked.Token);
            // Surface a slow response at the service-plan threshold while allowing a cold engine to finish.
            // Source editing and explicit saving remain available; the stale-result guards still apply.
            var started = System.Diagnostics.Stopwatch.StartNew();
            if (timeout > TimeSpan.FromMilliseconds(400))
            {
                var feedback = Task.Delay(TimeSpan.FromMilliseconds(400), linked.Token);
                if (await Task.WhenAny(pending, feedback) == feedback && !pending.IsCompleted && IsCurrent())
                { block.IsBrailleLocked = true; block.State = "응답 지연 · 점자 잠김 · 원문 편집과 저장 가능"; }
            }
            var remaining = timeout - started.Elapsed;
            var result = await pending.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, cancellationToken);
            if (!IsCurrent())
            {
                if (generations[block] == generation) block.State = "수정됨 · 재점역 필요";
                return TranslationOutcome.Superseded;
            }
            // Even an identical new translation requires an explicit human recheck.
            block.InvalidateBrailleConfirmations();
            block.Braille = result.Cells;
            block.Translation = result with { SourceText = block.Source };
            block.HasManualBraille = false;
            block.IsBrailleLocked = false;
            block.State = result.Blocked ? "점역 불가 · 원문 확인" : result.Dropped.Length != 0 ? "누락 문자 · 검토 필요" : "점역됨 · 검수 전";
            return TranslationOutcome.Applied;
        }
        catch (TimeoutException)
        {
            linked.Cancel();
            if (!IsCurrent()) return TranslationOutcome.Superseded;
            block.IsBrailleLocked = true;
            block.State = "응답 지연 · 점자 잠김";
            return TranslationOutcome.TimedOut;
        }
        catch (OperationCanceledException)
        {
            linked.Cancel();
            if (generations[block] == generation) block.State = "점역 취소됨";
            return TranslationOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return TranslationOutcome.Superseded;
            block.IsBrailleLocked = true;
            block.State = ex is Semojum.Core.Integration.SidecarException ? ex.Message : "점역 실패 · 점자 잠김";
            return TranslationOutcome.Failed;
        }
        finally { linked.Cancel(); }
    }
}
