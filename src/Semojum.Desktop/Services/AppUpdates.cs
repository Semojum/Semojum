using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;
public static class AppUpdates
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Semojum", "updates");
    public static async Task DownloadAsync(AppVersionInfo info, IProgress<string> progress, CancellationToken ct)
    {
        var url = info.DownloadUrl ?? throw new InvalidOperationException("업데이트 다운로드 주소가 아직 등록되지 않았습니다.");
        Directory.CreateDirectory(Root); var temporary = Path.Combine(Root, "download-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            HttpResponseMessage? response = null;
            try
            {
                for (var redirects = 0; redirects <= 5; redirects++)
                {
                    response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) break;
                    var location = response.Headers.Location; response.Dispose(); response = null;
                    if (location is null || redirects == 5) throw new InvalidDataException("업데이트 주소를 확인할 수 없습니다.");
                    url = new Uri(url, location); if (url.Scheme != "https" || url.UserInfo.Length != 0) throw new InvalidDataException("HTTPS 업데이트 주소가 필요합니다.");
                }
                response!.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 1024L * 1024 * 1024) throw new InvalidDataException("업데이트 파일이 너무 큽니다.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920]; long count = 0, lastReport = 0; int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        count += read; if (count > 1024L * 1024 * 1024) throw new InvalidDataException("업데이트 파일이 너무 큽니다.");
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        if (count - lastReport >= 1024 * 1024) { lastReport = count; progress.Report($"업데이트 다운로드 {count / 1024 / 1024} MB"); }
                    }
                }
            }
            finally { response?.Dispose(); }
            progress.Report("업데이트 파일을 확인하는 중…");
            await PortableUpdate.StageAsync(temporary, Root, info.Latest, ct);
            progress.Report($"버전 {info.Latest} 준비 완료 · 문서를 저장하고 앱을 다시 실행하면 적용됩니다.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<bool> ActivateAsync(Version current, string[] args)
    {
        var path = Path.Combine(Root, "pending.json");
        if (!File.Exists(path)) return false;
        try
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) return false;
            var pending = JsonSerializer.Deserialize<PendingUpdate>(await File.ReadAllTextAsync(path));
            if (pending is null || !Version.TryParse(pending.Version, out var version) || version <= current) return false;
            var directory = Path.Combine(Root, version.ToString(3));
            foreach (var required in new[] { "Semojum.Mockup.exe", "Semojum.Mockup.dll", "engine/semojum-engine.exe" }) if (!pending.Hashes.ContainsKey(required)) return false;
            foreach (var item in pending.Hashes)
            {
                PortableUpdate.ValidatePath(item.Key);
                if (await PortableUpdate.HashAsync(Path.Combine(directory, item.Key)) != item.Value) return false;
            }
            var dll = AssemblyName.GetAssemblyName(Path.Combine(directory, "Semojum.Mockup.dll"));
            if (dll.Name != "Semojum.Mockup" || dll.Version?.ToString(3) != version.ToString(3)) return false;
            var start = new ProcessStartInfo(Path.Combine(directory, "Semojum.Mockup.exe")) { WorkingDirectory = directory, UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            Process.Start(start); return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or BadImageFormatException or System.ComponentModel.Win32Exception) { return false; }
    }
}
