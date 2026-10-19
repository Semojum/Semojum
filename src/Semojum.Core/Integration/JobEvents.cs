using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Semojum.Core.Integration;

public sealed record JobEvent(string Name, JsonElement Data);
public static class ServerSentEvents
{
    public static async IAsyncEnumerable<JobEvent> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);
        var name = "message"; var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length > 1024 * 1024) throw new InvalidDataException("진행 이벤트가 너무 큽니다.");
            if (line.Length == 0)
            {
                if (data.Length > 0 && name is "page_done" or "job_done" or "queue_position")
                {
                    using var json = JsonDocument.Parse(data.ToString());
                    yield return new(name, json.RootElement.Clone());
                }
                data.Clear(); name = "message"; continue;
            }
            if (line.StartsWith(':')) continue;
            var colon = line.IndexOf(':'); var field = colon < 0 ? line : line[..colon]; var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            if (field == "event") name = value;
            else if (field == "data") { if (data.Length > 0) data.Append('\n'); data.Append(value); if (data.Length > 4 * 1024 * 1024) throw new InvalidDataException("진행 이벤트가 너무 큽니다."); }
        }
    }
}

/// <summary>One bounded wake-up signal; repeated/replayed events never append duplicate document blocks.</summary>
public sealed class JobEventWatch : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime;
    private readonly SemaphoreSlim changed = new(0, 1);
    private readonly Task loop;
    public JobEventWatch(SemojumApiClient api, string jobId, CancellationToken ct)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        loop = WatchAsync(api, jobId);
    }
    private async Task WatchAsync(SemojumApiClient api, string jobId)
    {
        var retry = 1;
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                await foreach (var item in api.EventsAsync(jobId, lifetime.Token).ConfigureAwait(false))
                {
                    retry = 1; if (changed.CurrentCount == 0) changed.Release();
                    if (item.Name == "job_done") return;
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException) { }
            catch (SemojumApiException ex) when (ex.Status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound) { return; }
            catch (SemojumApiException) { }
            try { await Task.Delay(TimeSpan.FromSeconds(retry), lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            retry = Math.Min(retry * 2, 30);
        }
    }
    public Task<bool> WaitAsync(CancellationToken ct) => changed.WaitAsync(TimeSpan.FromSeconds(3), ct);
    public async ValueTask DisposeAsync() { lifetime.Cancel(); try { await loop.ConfigureAwait(false); } finally { lifetime.Dispose(); changed.Dispose(); } }
}
