using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Semojum.Core.Integration;

public sealed record SidecarCommand(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory = null);
public sealed class SidecarException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

/// <summary>Owns one stateless JSON-lines engine process. A cancelled exchange discards that process.</summary>
public sealed class BrailleSidecar(SidecarCommand command) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Process? process;
    private Task? stderrDrain;
    private long nextId;
    private bool disposed;
    public string? EngineVersion { get; private set; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public async Task<JsonElement> InvokeAsync(string operation, object? fields = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ObjectDisposedException.ThrowIf(disposed, this);
        var request = fields is null ? new JsonObject() : JsonSerializer.SerializeToNode(fields) as JsonObject
            ?? throw new ArgumentException("사이드카 요청 필드는 JSON 객체여야 합니다.", nameof(fields));
        if (request.ContainsKey("id") || request.ContainsKey("op"))
            throw new ArgumentException("id와 op는 연결부에서 지정합니다.", nameof(fields));
        var deadline = timeout ?? TimeSpan.FromSeconds(10);
        if (deadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await EnsureStartedAsync(linked.Token).ConfigureAwait(false);
            return await ExchangeWithTimeoutAsync(operation, request, deadline, linked.Token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (process is { HasExited: false }) return;
        await StopAsync().ConfigureAwait(false);
        var start = new ProcessStartInfo(command.Executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
        if (command.WorkingDirectory is { } folder) start.WorkingDirectory = folder;
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        process = Process.Start(start) ?? throw new SidecarException("점역 엔진을 시작하지 못했습니다.");
        // Drain without retaining document text or potentially sensitive engine diagnostics.
        stderrDrain = DrainAsync(process.StandardError);
        try
        {
            var ping = await ExchangeWithTimeoutAsync("ping", new(), StartupTimeout, cancellationToken).ConfigureAwait(false);
            if (!ping.TryGetProperty("protocol", out var protocol) || !protocol.TryGetInt32(out var version) || version != 1
                || !ping.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                throw new SidecarException("지원하지 않는 점역 엔진 프로토콜입니다.", "PROTOCOL_MISMATCH");
            EngineVersion = ping.TryGetProperty("version", out var engineVersion) ? engineVersion.GetString() : null;
        }
        catch { await StopAsync().ConfigureAwait(false); throw; }
    }

    private async Task<JsonElement> ExchangeWithTimeoutAsync(string operation, JsonObject fields,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var id = ++nextId;
            fields["id"] = id; fields["op"] = operation;
            await process!.StandardInput.WriteLineAsync(fields.ToJsonString().AsMemory(), deadline.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
            var line = await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false)
                ?? throw new IOException("점역 엔진이 응답 전에 종료되었습니다.");
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var responseId)
                || !responseId.TryGetInt64(out var number) || number != id)
                throw new IOException("점역 엔진 응답의 요청 번호가 일치하지 않습니다.");
            if (root.TryGetProperty("error", out var error))
                throw new SidecarException(error.GetString() ?? "점역 엔진 오류",
                    root.TryGetProperty("code", out var code) ? code.GetString() : null);
            return root.Clone();
        }
        catch (SidecarException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync().ConfigureAwait(false);
            throw new TimeoutException("점역 엔진 응답 시간이 초과되었습니다. 다시 시도하면 엔진을 새로 시작합니다.");
        }
        catch { await StopAsync().ConfigureAwait(false); throw; }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task StopAsync()
    {
        var old = process; process = null; EngineVersion = null;
        if (old is null) return;
        try
        {
            if (!old.HasExited) old.Kill(entireProcessTree: true);
            await old.WaitForExitAsync().ConfigureAwait(false);
            if (stderrDrain is not null) await stderrDrain.ConfigureAwait(false);
        }
        finally { old.Dispose(); stderrDrain = null; }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        try { await StopAsync().ConfigureAwait(false); }
        finally { gate.Release(); }
    }
}
