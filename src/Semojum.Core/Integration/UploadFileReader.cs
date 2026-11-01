using System.IO;

namespace Semojum.Core.Integration;

public static class UploadFileReader
{
    public static void ValidateUniqueFileNames(IReadOnlyList<string> paths)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            if (!names.Add(Path.GetFileName(path)))
                throw new ArgumentException("이름이 같은 파일은 함께 올릴 수 없습니다. 파일 이름을 다르게 변경해 주세요.");
    }

    public static async Task<IReadOnlyList<UploadFile>> ReadAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ValidateUniqueFileNames(paths);
        // The limit applies to each file (Figma C3, server rule), so every file is checked against the full limit.
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(path).Length > JobClient.MaximumUploadBytes) throw TooLarge(path);
        }
        var files = new List<UploadFile>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length > JobClient.MaximumUploadBytes) throw TooLarge(path);
            var bytes = await ReadBoundedAsync(source, JobClient.MaximumUploadBytes, cancellationToken);
            files.Add(new(Path.GetFileName(path), bytes));
        }
        return files;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920]; long readTotal = 0;
        while (true)
        {
            var count = (int)Math.Min(chunk.Length, limit - readTotal + 1);
            var read = await source.ReadAsync(chunk.AsMemory(0, count), cancellationToken);
            if (read == 0) break;
            if (read > limit - readTotal) throw TooLarge();
            buffer.Write(chunk, 0, read); readTotal += read;
        }
        return buffer.ToArray();
    }
    private static ArgumentException TooLarge(string? path = null) =>
        new((path is null ? "" : $"{Path.GetFileName(path)} 파일이 100 MB를 넘습니다. ") + "파일 1개당 100 MB까지 올릴 수 있습니다. 파일 크기를 줄이거나 나누어 다시 올려 주세요.");
}
