namespace Semojum.Core;

/// <summary>A short lived writer lease beside the work file, also honored across app processes.</summary>
public sealed class WorkFileLease : IDisposable
{
    private readonly FileStream stream;
    private WorkFileLease(FileStream stream) => this.stream = stream;
    public static WorkFileLease? TryAcquire(string path)
    {
        try { return new(new FileStream(Path.GetFullPath(path) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    public void Dispose() => stream.Dispose();
}
