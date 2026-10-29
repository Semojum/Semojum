using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;
public partial class MainWindow
{
    private int lastPreviewPage = 1;
    private async Task OptimizeDocumentAsync()
    {
        var dialog = new OutputSettingsDialog(session.Layout, accountPreferences.Output) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.Remember) { accountPreferences = accountPreferences with { Output = dialog.Result }; await PersistAccountPreferencesAsync(); }
        await ShowCompositionAsync(1, dialog.Result);
    }
    private async Task ShowCompositionAsync(int face = 1, OutputPreferences? output = null)
    {
        if (engine is not LocalBrailleEngine local) throw new ArgumentException("샘플 문서는 조판할 수 없습니다.");
        if (fileBusy) return;
        fileBusy = true;
        try
        {
            var snapshot = SmjFile.Snapshot(session).Manifest;
            if (output is not null) snapshot = snapshot with { Layout = output.Layout ?? new(), Options = snapshot.Options with { LineBreakUnit = output.LineBreakUnit } };
            var range = new ExportRangeDialog(DocumentExport.OriginalNumbers(snapshot), null, format: ".txt — 원문") { Owner = this };
            if (range.ShowDialog() != true) return;
            snapshot = DocumentExport.SelectOriginal(snapshot, range.Result);
            DocumentExport.RequireCompletedPages(snapshot);
            var selectedIds = snapshot.Blocks.Select(b => b.Id).ToHashSet();
            if (session.Blocks.Where(b => selectedIds.Contains(b.Id)).Any(b => b.IsBrailleLocked ||
                b.Source.Length > 0 && b.Braille.Length == 0 && !LocalBrailleEngine.IsLayoutMarker(b.Source) && !LocalBrailleEngine.IsNonPrintingTag(b.Source) ||
                b.Source.Length > 0 && !b.HasManualBraille && translatedRevisions.GetValueOrDefault(b, -1) != b.SourceRevision))
                throw new ArgumentException("먼저 원문·옵션이 바뀐 블록을 재점역해 주세요. 점역 전 내용은 미리보기에 포함하지 않습니다.");
            session.Status = "점자 면을 조판하는 중…";
            var result = await local.ComposeAsync(snapshot, documentLifetime.Token);
            if (result.Replaced.Count > 0) throw new ArgumentException("조판에서 빈칸으로 바뀌는 문자가 있습니다: " + string.Join(", ", result.Replaced.Keys) + ". 편집 화면에서 확인해 주세요.");
            var pages = await local.ReadBrfAsync(result.Brf, documentLifetime.Token);
            if (face < 1 || face > pages.Length) throw new ArgumentException($"1–{pages.Length}면 범위로 입력해 주세요.");
            var dialog = new BraillePreviewDialog(pages, result.Brf, snapshot.Layout, session.Title, face, result.SpacingUnknownBlockIds, result.AlignmentUnknownBlockIds) { Owner = this };
            dialog.ShowDialog(); lastPreviewPage = dialog.Page;
            session.Status = $"조판 결과 {pages.Length}면 · 마지막 확인 {dialog.Page}면";
        }
        finally { fileBusy = false; }
    }
}
