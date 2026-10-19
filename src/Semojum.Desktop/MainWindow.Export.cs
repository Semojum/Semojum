using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private async Task ExportAsync()
    {
        if (fileBusy || session.Blocks.Count == 0) return;
        var dialog = new SaveFileDialog { Title = "문서 내보내기", FileName = Path.GetFileNameWithoutExtension(session.Title),
            Filter = "BRF 점자문서|*.brf|원문 텍스트|*.txt|점자 텍스트 (판면 포함)|*.txt", DefaultExt = ".brf" };
        if (dialog.ShowDialog(this) != true) return;
        fileBusy = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime.Token);
        var ct = cancellation.Token;
        try
        {
            var owner = session;
            var snapshot = SmjFile.Snapshot(owner).Manifest;
            var invalid = owner.Blocks.Where(b => b.IsBrailleLocked || b.Braille.Length == 0 && b.Source.Length > 0 && !LocalBrailleEngine.IsLayoutMarker(b.Source)
                || !b.HasManualBraille && translatedRevisions.GetValueOrDefault(b, -1) != b.SourceRevision).Select(b => b.Id).ToHashSet();
            var plainText = dialog.FilterIndex == 2;
            var local = engine as LocalBrailleEngine;
            string? fullBrf = null;
            async Task<string> BuildAsync(SmjManifest selected)
            {
                DocumentExport.RequireCompletedPages(selected);
                if (selected.Blocks.Any(b => invalid.Contains(b.Id)))
                    throw new InvalidOperationException("선택한 범위에 점역하지 못했거나 원문·옵션 변경 후 점역이 필요한 블록이 있습니다. 재점역 후 내보내 주세요.");
                if (local is null) throw new InvalidOperationException("샘플 엔진에서는 점자 문서를 내보낼 수 없습니다.");
                var result = await local.ComposeAsync(selected, ct);
                if (result.Replaced.Count > 0 && MessageBox.Show(this, "BRF에서 빈칸으로 바뀌는 문자: " + string.Join(", ", result.Replaced.Select(p => $"{p.Key}({p.Value})")) + "\n계속 내보내시겠습니까?", "문자 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) throw new OperationCanceledException("내보내기를 취소했습니다.");
                return result.Brf;
            }
            async Task<int> CountAsync()
            {
                fullBrf ??= await BuildAsync(snapshot);
                return DocumentExport.BrfPages(fullBrf).Length;
            }
            var ranges = new ExportRangeDialog(DocumentExport.OriginalNumbers(snapshot), plainText ? null : CountAsync) { Owner = this };
            if (ranges.ShowDialog() != true) return;
            var range = ranges.Result;
            var selected = DocumentExport.SelectOriginal(snapshot, range);
            DocumentExport.RequireCompletedPages(selected);
            string text;
            if (plainText) text = string.Join("\r\n", selected.Blocks.Select(b => b.Source));
            else
            {
                if (selected.Blocks.Any(b => b.Translation?.NeedsReview == true)
                    && MessageBox.Show(this, "선택한 범위에 누락 문자 또는 점역 불가 표시가 있습니다. 현재 점자로 내보내시겠습니까?", "점역 결과 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var brf = range.Scope == ExportScope.OriginalPages ? await BuildAsync(selected) : fullBrf ?? await BuildAsync(snapshot);
                brf = DocumentExport.SelectBraille(brf, range);
                text = dialog.FilterIndex == 1 ? brf : DocumentExport.BrailleText(await local!.ReadBrfAsync(brf, ct));
            }
            ct.ThrowIfCancellationRequested();
            var temp = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), ct);
                ct.ThrowIfCancellationRequested();
                File.Move(temp, dialog.FileName, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            if (session == owner) session.Status = "선택한 범위를 내보냈습니다. 작업 파일 저장은 Ctrl+S를 눌러 주세요.";
        }
        finally { cancellation.Cancel(); fileBusy = false; }
    }
}
