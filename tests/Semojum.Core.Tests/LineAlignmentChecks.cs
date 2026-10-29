using Semojum.Core;
using Semojum.Core.Integration;

internal static class LineAlignmentChecks
{
    public static async Task RunAsync()
    {
        var python = Environment.GetEnvironmentVariable("SEMOJUM_ENGINE_PYTHON");
        if (string.IsNullOrEmpty(python)) { Console.WriteLine("SKIP line alignment: SEMOJUM_ENGINE_PYTHON is not set"); return; }
        var entry = Path.Combine(AppContext.BaseDirectory, "engine-adapter", "engine-entry.py");
        await using var sidecar = new BrailleSidecar(new(python, ["-u", entry])); var engine = new LocalBrailleEngine(sidecar);
        var source = "가\n<!가운데정렬>나\n<!오른쪽정렬>다";
        var block = new EditorBlock { Id = 1, Page = 1, Source = source };
        var outcome = await new TranslationCoordinator(engine).TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(20));
        if (outcome != TranslationOutcome.Applied) throw new Exception("Tagged text translation failed");
        var local = Environment.GetEnvironmentVariable("SEMOJUM_EXPECT_LINE_ALIGNMENT") == "1";
        if (local && block.Translation?.LineAlignment is not [{ Align: "" }, { Align: "center" }, { Align: "right" }]) throw new Exception("Verified engine line alignment is missing");
        if (!local && block.Translation?.LineAlignment is not null) throw new Exception("Legacy engine unexpectedly returned a line map");
        var model = new DocumentSession { Layout = new(Columns: 8, Lines: 4, PageNumbers: "출력 안 함", Footer: "출력 안 함", ShowSourcePageNumber: false, ShowBraillePageNumber: false) };
        model.Add(block);
        var path = Path.Combine(Path.GetTempPath(), "semojum-alignment-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); var loaded = await SmjFile.LoadAsync(path);
            if (loaded.ActiveBlock!.Source != source || loaded.ActiveBlock.Braille != block.Braille) throw new Exception("SMJ changed source tags or braille");
            var result = await engine.ComposeAsync(SmjFile.Snapshot(loaded).Manifest);
            if (local)
            {
                if (loaded.ActiveBlock.Translation?.LineAlignment is not { Length: 3 } || result.AlignmentUnknownBlockIds is not { Length: 0 }) throw new Exception("Verified alignment did not survive SMJ reopen");
                if (result.Pages![0].Lines.Take(3).ToArray() is not ["⠀⠀⠫", "⠀⠀⠀⠉", "⠀⠀⠀⠀⠀⠀⠀⠊"]) throw new Exception("Distinct line alignments did not appear in the composed BRF");
            }
            else if (result.AlignmentUnknownBlockIds is not [1]) throw new Exception("Old engine was not reported as unverified");
            var manual = loaded.ActiveBlock; manual.HasManualBraille = true; var manualCells = manual.Braille;
            var manualResult = await engine.ComposeAsync(SmjFile.Snapshot(loaded).Manifest);
            if (manual.Braille != manualCells || manualResult.AlignmentUnknownBlockIds is not [1]) throw new Exception("Manual braille was changed or guessed");
            if (local)
            {
                manual.HasManualBraille = false; manual.Source += "수정";
                var stale = await engine.ComposeAsync(SmjFile.Snapshot(loaded).Manifest);
                if (stale.AlignmentUnknownBlockIds is not [1]) throw new Exception("A stale source map was applied");
                manual.Source = source; manual.Translation = manual.Translation! with { LineAlignment = [new(0, 1, 0, 999, "right")] };
                var invalid = await engine.ComposeAsync(SmjFile.Snapshot(loaded).Manifest);
                if (invalid.AlignmentUnknownBlockIds is not [1]) throw new Exception("Invalid saved line alignment was trusted");
            }
            foreach (var unusual in new[] { "가😀\n<!가운데정렬>나", "가\r\n<!오른쪽정렬>나", "가\n\n<!가운데정렬>나", "<!왼쪽정렬>가<!오른쪽정렬>나" })
            {
                var other = new EditorBlock { Id = 2, Page = 1, Source = unusual };
                var translated = await new TranslationCoordinator(engine).TranslateAsync(other, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(20));
                if (translated != TranslationOutcome.Applied || other.Translation?.LineAlignment is not null) throw new Exception("Unsafe Unicode, newlines or ambiguous tags produced a verified mapping");
                var otherModel = new DocumentSession { Layout = model.Layout }; otherModel.Add(other);
                var otherResult = await engine.ComposeAsync(SmjFile.Snapshot(otherModel).Manifest);
                if (otherResult.AlignmentUnknownBlockIds is not [2]) throw new Exception("Unsupported alignment was not reported as unverified");
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
