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
        long expectedRemaining = JobClient.MaximumUploadBytes;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = new FileInfo(path).Length;
            if (length > expectedRemaining) throw TooLarge();
            expectedRemaining -= length;
        }
        var files = new List<UploadFile>();
        long remaining = JobClient.MaximumUploadBytes;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length > remaining) throw TooLarge();
            var bytes = await ReadBoundedAsync(source, remaining, cancellationToken);
            remaining -= bytes.LongLength;
            files.Add(new(Path.GetFileName(path), bytes));
        }
        return files;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, long remaining, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920]; long readTotal = 0;
        while (true)
        {
            var count = (int)Math.Min(chunk.Length, remaining - readTotal + 1);
            var read = await source.ReadAsync(chunk.AsMemory(0, count), cancellationToken);
            if (read == 0) break;
            if (read > remaining - readTotal) throw TooLarge();
            buffer.Write(chunk, 0, read); readTotal += read;
        }
        return buffer.ToArray();
    }
    private static ArgumentException TooLarge() => new("업로드 전체 크기는 100 MB까지입니다. 파일 크기를 줄이거나 나누어 다시 올려 주세요.");
}
