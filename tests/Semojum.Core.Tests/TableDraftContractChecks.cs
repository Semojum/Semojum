using Semojum.Core;
using Semojum.Core.Integration;

internal static class TableDraftContractChecks
{
    public static async Task RunAsync()
    {
        var python = Environment.GetEnvironmentVariable("SEMOJUM_ENGINE_PYTHON");
        if (string.IsNullOrEmpty(python)) { Console.WriteLine("SKIP table draft sidecar fixture: SEMOJUM_ENGINE_PYTHON is not set"); return; }
        var script = Path.Combine(Path.GetTempPath(), "semojum-table-draft-" + Guid.NewGuid().ToString("N") + ".py");
        await File.WriteAllTextAsync(script, """
import json, sys
for line in sys.stdin:
    req = json.loads(line)
    if req.get('op') == 'ping':
        result = {'id': req['id'], 'ok': True, 'version': 'fixture', 'protocol': 1}
    else:
        draft = {'render_mode': 'table_grid', 'label': 'test', 'cells': '\u2801', 'breaks': []}
        if req.get('draft_text') and req.get('table_source') != 'legacy':
            draft['source_text'] = '<!표>\n<!행><!칸>가<!/행>\n<!/표>'
        result = {'id': req['id'], 'cells': '\u2801', 'breaks': [], 'dropped': [], 'blocked': False, 'drafts': [draft]}
    print(json.dumps(result), flush=True)
""");
        try
        {
            await using var sidecar = new BrailleSidecar(new(python, ["-u", script])); var engine = new LocalBrailleEngine(sidecar);
            var next = await engine.TranslateElementAsync("표", new(Type: "table", TableSource: "new"), new());
            if (next.Drafts is not [{ SourceText: "<!표>\n<!행><!칸>가<!/행>\n<!/표>" }]) throw new Exception("Opt-in draft source was not decoded");
            var legacy = await engine.TranslateElementAsync("표", new(Type: "table", TableSource: "legacy"), new());
            if (legacy.Drafts is not [{ SourceText: null }]) throw new Exception("Legacy draft without source text was not preserved");
            var model = new DocumentSession(); model.Add(new EditorBlock { Id = 1, Page = 1, Source = "표", Braille = next.Cells, Translation = next });
            var snapshot = SmjFile.Snapshot(model);
            if (snapshot.Manifest.Version != SmjFile.Version || snapshot.Manifest.Blocks[0].Translation?.Drafts[0].SourceText != next.Drafts[0].SourceText)
                throw new Exception("SMJ did not retain optional draft source without changing its version");
        }
        finally { if (File.Exists(script)) File.Delete(script); }
    }
}
