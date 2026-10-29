using Semojum.Core;
using Semojum.Core.Integration;

internal static class SourceLineMapChecks
{
    public static async Task RunAsync()
    {
        var python = Environment.GetEnvironmentVariable("SEMOJUM_ENGINE_PYTHON");
        if (string.IsNullOrEmpty(python)) { Console.WriteLine("SKIP source-map sidecar fixture: SEMOJUM_ENGINE_PYTHON is not set"); return; }
        var path = Path.Combine(Path.GetTempPath(), "semojum-map-" + Guid.NewGuid().ToString("N") + ".py");
        await File.WriteAllTextAsync(path, """
import json, sys
for line in sys.stdin:
    request = json.loads(line)
    if request.get('op') == 'ping':
        result = {'id': request['id'], 'ok': True, 'version': 'fixture', 'protocol': 1}
    else:
        result = {'id': request['id'], 'cells': '\u2800\u2800\u282b\n\u2809', 'breaks': [], 'dropped': [], 'blocked': False, 'drafts': []}
        if request.get('source_map') and request.get('text') == '\uac00\n\ub098':
            result['source_line_map'] = [{'source_start': 0, 'source_end': 1, 'braille_start': 2, 'braille_end': 3}, {'source_start': 2, 'source_end': 3, 'braille_start': 4, 'braille_end': 5}]
        elif request.get('source_map') and request.get('text') == '\ub2e4\n\ub77c':
            result['source_line_map'] = [{'source_start': 0, 'source_end': 999, 'braille_start': 2, 'braille_end': 3}]
    print(json.dumps(result), flush=True)
""");
        try
        {
            await using var sidecar = new BrailleSidecar(new(python, ["-u", path]));
            var engine = new LocalBrailleEngine(sidecar);
            var block = new EditorBlock { Id = 1, Page = 1, Source = "가\n나" };
            var outcome = await new TranslationCoordinator(engine).TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(5));
            if (outcome != TranslationOutcome.Applied || block.Translation?.SourceLineMap is not [{ SourceStart: 0, SourceEnd: 1, BrailleStart: 2, BrailleEnd: 3 }, { SourceStart: 2, SourceEnd: 3, BrailleStart: 4, BrailleEnd: 5 }]
                || block.Translation.SourceMapStamp != new SourceMapStamp(block.SourceRevision, block.BrailleRevision, block.MetadataRevision))
                throw new Exception("The mapped translation must be tied to the exact applied block revisions.");
            var model = new DocumentSession(); model.Add(block);
            var saved = SmjFile.Snapshot(model);
            if (saved.Manifest.Version != 4 || saved.Manifest.Blocks[0].Translation?.SourceLineMap is not null)
                throw new Exception("The temporary mapping must not alter or persist in a normal SMJ file.");
            var malformed = await engine.TranslateElementAsync("다\n라", new(), new());
            var legacy = await engine.TranslateElementAsync("마\n바", new(), new());
            if (malformed.SourceLineMap is not null || legacy.SourceLineMap is not null)
                throw new Exception("Out-of-range or absent engine mappings must be treated as unavailable.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        if (Environment.GetEnvironmentVariable("SEMOJUM_EXPECT_SOURCE_MAP") == "1")
        {
            var entry = Path.Combine(AppContext.BaseDirectory, "engine-adapter", "engine-entry.py");
            await using var sidecar = new BrailleSidecar(new(python, ["-u", entry]));
            var engine = new LocalBrailleEngine(sidecar);
            var actual = new EditorBlock { Id = 2, Page = 1, Source = "가\n나" };
            var outcome = await new TranslationCoordinator(engine).TranslateAsync(actual, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(20));
            if (outcome != TranslationOutcome.Applied || actual.Translation?.SourceLineMap is not [{ SourceStart: 0, SourceEnd: 1 }, { SourceStart: 2, SourceEnd: 3 }]
                || actual.Translation.SourceMapStamp != new SourceMapStamp(actual.SourceRevision, actual.BrailleRevision, actual.MetadataRevision))
                throw new Exception("The local checked-out engine did not return the expected verified line mapping.");
        }
    }
}
