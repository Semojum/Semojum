namespace Semojum.Core;

/// <summary>A short lived writer lease beside the work file, also honored across app processes.</summary>
public sealed class WorkFileLease : IDisposable
{
    private readonly FileStream stream;
    private WorkFileLease(FileStream stream) => this.stream = stream;
    public static WorkFileLease? TryAcquire(string path) => TryAcquire(path, out _);
    /// <param name="accessDenied">True when the lock could not be created for lack of write permission rather than because another writer holds it.</param>
    public static WorkFileLease? TryAcquire(string path, out bool accessDenied)
    {
        accessDenied = false;
        try { return new(new FileStream(Path.GetFullPath(path) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)); }
        catch (UnauthorizedAccessException) { accessDenied = true; return null; }
        catch (IOException) { return null; }
    }
    public void Dispose() => stream.Dispose();
}
