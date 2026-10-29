using System.Reflection;
using Semojum.Core.Integration;

internal static class UploadSafetyChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Upload rejects equal image filenames before reading any data", DuplicateNames);
        yield return ("Upload rejects files that grew after selection and keeps input files untouched", GrownFile);
        yield return ("Upload checks actual bytes while reading and preserves selected order", BoundedRead);
    }

    private static string Folder() { var p = Path.Combine(Path.GetTempPath(), "semojum-upload-safety-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    private static async Task DuplicateNames()
    {
        var root = Folder();
        try
        {
            var a = Path.Combine(root, "a"); var b = Path.Combine(root, "b"); Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            var first = Path.Combine(a, "same.png"); var second = Path.Combine(b, "SAME.png");
            await File.WriteAllBytesAsync(first, [1]); await File.WriteAllBytesAsync(second, [2]);
            try { await UploadFileReader.ReadAsync([first, second]); throw new Exception("Duplicate filename accepted"); }
            catch (ArgumentException ex) when (ex.Message.Contains("이름")) { }
            if ((await File.ReadAllBytesAsync(first))[0] != 1 || (await File.ReadAllBytesAsync(second))[0] != 2) throw new Exception("Input altered");
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task GrownFile()
    {
        var root = Folder();
        try
        {
            var path = Path.Combine(root, "grown.png");
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) f.SetLength(JobClient.MaximumUploadBytes + 1);
            try { await UploadFileReader.ReadAsync([path]); throw new Exception("Grown file accepted"); }
            catch (ArgumentException ex) when (ex.Message.Contains("100")) { }
            if (new FileInfo(path).Length != JobClient.MaximumUploadBytes + 1) throw new Exception("Input altered");
            var second = Path.Combine(root, "second.png");
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Write)) f.SetLength(JobClient.MaximumUploadBytes);
            await File.WriteAllBytesAsync(second, [1]);
            try { await UploadFileReader.ReadAsync([path, second]); throw new Exception("Oversized aggregate accepted"); }
            catch (ArgumentException ex) when (ex.Message.Contains("100")) { }
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task BoundedRead()
    {
        var root = Folder();
        try
        {
            var first = Path.Combine(root, "b.png"); var second = Path.Combine(root, "a.png");
            await File.WriteAllBytesAsync(first, [2, 3]); await File.WriteAllBytesAsync(second, [1]);
            var files = await UploadFileReader.ReadAsync([first, second]);
            if (files.Count != 2 || files[0].FileName != "b.png" || !files[0].Bytes.SequenceEqual(new byte[] {2, 3}) || files[1].FileName != "a.png") throw new Exception("Order or content changed");
            var method = typeof(UploadFileReader).GetMethod("ReadBoundedAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var exact = (Task<byte[]>)method.Invoke(null, [new MemoryStream(new byte[] { 1, 2, 3 }), 3L, CancellationToken.None])!;
            if (!(await exact).SequenceEqual(new byte[] { 1, 2, 3 })) throw new Exception("Exact byte limit was not accepted");
            var task = (Task<byte[]>)method.Invoke(null, [new MemoryStream(new byte[] { 1, 2, 3, 4 }), 3L, CancellationToken.None])!;
            try { await task; throw new Exception("Actual bytes beyond remaining aggregate limit accepted"); }
            catch (ArgumentException ex) when (ex.Message.Contains("100")) { }
        }
        finally { Directory.Delete(root, true); }
    }
}
