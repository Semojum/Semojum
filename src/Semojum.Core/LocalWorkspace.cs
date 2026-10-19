using System.Text.Json;

namespace Semojum.Core;

public sealed record EditorPreferences(int Version = 1, string[]? RecentFiles = null, string? WorkFolder = null,
    string SixDotKeys = "F D S J K L", bool SpaceEnabled = true, bool EnterEnabled = true, bool AutoCheckUpdates = true);
public sealed record WorkspaceEntry(string Path, string Name, bool IsDirectory, DateTime ModifiedAt, long Size)
{
    public string ModifiedLabel => ModifiedAt.ToString("yyyy-MM-dd HH:mm");
    public string SizeLabel => IsDirectory ? "폴더" : Size < 1024 ? $"{Size} B" : Size < 1024 * 1024 ? $"{Size / 1024d:0.#} KB" : $"{Size / (1024d * 1024):0.#} MB";
}

/// <summary>Local navigation/settings only. Never stores document contents or credentials.</summary>
public sealed class LocalWorkspace(string preferencesPath)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim writeGate = new(1, 1);
    public const int RecentLimit = 50;
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public string? LoadWarning { get; private set; }

    public EditorPreferences Load()
    {
        LoadWarning = null;
        try
        {
            if (!File.Exists(preferencesPath)) return new();
            if (new FileInfo(preferencesPath).Length > 1024 * 1024) throw new InvalidDataException("설정 파일 크기를 초과했습니다.");
            var value = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(preferencesPath), Json);
            if (value is null || value.Version != 1) throw new InvalidDataException("지원하지 않는 설정 파일입니다.");
            ArgumentException.ThrowIfNullOrWhiteSpace(value.SixDotKeys);
            new SixDotInput().Configure(value.SixDotKeys);
            return value with { RecentFiles = NormalizeRecent(value.RecentFiles ?? []) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LoadWarning = "로컬 설정을 읽지 못해 기본값으로 시작합니다: " + ex.Message;
            return new();
        }
    }

    public async Task SaveAsync(EditorPreferences preferences, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferences.SixDotKeys);
        new SixDotInput().Configure(preferences.SixDotKeys);
        var data = JsonSerializer.SerializeToUtf8Bytes(preferences with { Version = 1, RecentFiles = NormalizeRecent(preferences.RecentFiles ?? []) }, Json);
        await writeGate.WaitAsync(ct);
        var temporary = preferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(preferencesPath))!);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                await file.WriteAsync(data, ct); await file.FlushAsync(ct); file.Flush(true);
            }
            ct.ThrowIfCancellationRequested(); File.Move(temporary, preferencesPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); writeGate.Release(); }
    }

    public static EditorPreferences Remember(EditorPreferences value, string path) =>
        value with { RecentFiles = NormalizeRecent(new[] { Path.GetFullPath(path) }.Concat(value.RecentFiles ?? [])) };
    public static EditorPreferences Forget(EditorPreferences value, string path) =>
        value with { RecentFiles = (value.RecentFiles ?? []).Where(p => !SamePath(p, path)).ToArray() };
    public static bool SamePath(string first, string second) => PathComparer.Equals(Path.GetFullPath(first), Path.GetFullPath(second));

    public static WorkspaceEntry[] ListFolder(string path)
    {
        var folder = new DirectoryInfo(path);
        // Enumerate only the selected directory. A user opens each subdirectory explicitly.
        return folder.EnumerateFileSystemInfos()
            .Where(info => info is DirectoryInfo || IsWorkFile(info.FullName))
            .Select(info => new WorkspaceEntry(info.FullName, info.Name, info is DirectoryInfo,
                info.LastWriteTime, info is FileInfo file ? file.Length : 0))
            .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static bool IsWorkFile(string path) => Path.GetExtension(path).Equals(".smj", StringComparison.OrdinalIgnoreCase);
    private static string[] NormalizeRecent(IEnumerable<string> paths) => paths.Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(Path.GetFullPath).Where(IsWorkFile).Distinct(PathComparer).Take(RecentLimit).ToArray();
}
