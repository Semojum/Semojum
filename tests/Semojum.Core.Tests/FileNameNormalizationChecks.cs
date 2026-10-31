using System.Text;
using Semojum.Core;

internal static class FileNameNormalizationChecks
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "nuridot-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var decomposedName = "가나다.smj".Normalize(NormalizationForm.FormD);
        var path = Path.Combine(root, decomposedName);
        try
        {
            var original = new DocumentSession { Title = decomposedName };
            original.Add(new EditorBlock { Id = 1, Page = 1, Source = "원문", Braille = "⠁" });
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(original));
            var loaded = await SmjFile.LoadAsync(path);
            if (loaded.Title != "가나다.smj" || loaded.WindowTitle != "가나다.smj — 누리닷")
                throw new Exception("Decomposed filename was not composed for the document display.");
            if (!Directory.EnumerateFiles(root).Select(Path.GetFileName).Contains(decomposedName, StringComparer.Ordinal))
                throw new Exception("The actual decomposed path was changed instead of preserved.");
            loaded.ActiveBlock = loaded.Blocks[0]; loaded.ActiveBlock.Source = "수정";
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(loaded));
            if ((await SmjFile.LoadAsync(path)).Blocks[0].Source != "수정") throw new Exception("Saving to the original path failed.");
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(root); }
    }
}
