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
    /// <summary>Checks receive the unshown notice and return the chosen action index instead of showing it.</summary>
    internal Func<NoticeDialog, int>? ExportConfirmation { get; set; }
    internal Func<ExportRangeDialog, bool>? ExportRangeSelection { get; set; }
    // 메시지창 19종의 틀(노란 상태 띠, 실제 동작 단추 + 「취소」)로 묻습니다. 첫 단추만 내보내기를 이어 갑니다.
    private bool ConfirmExport(string message, string detail, string action = "내보내기", double height = 196)
    {
        var notice = new NoticeDialog(message, detail, [action, "취소"], height: height);
        if (ExportConfirmation is { } decide) return decide(notice) == 0;
        notice.Owner = this; notice.ShowDialog(); return notice.Choice == 0;
    }
    private async Task ExportAsync(string? path = null, int format = 1, bool chooseRange = true)
    {
        if (fileBusy || session.Blocks.Count == 0) return;
        var dialog = new SaveFileDialog { Title = "문서 내보내기", FileName = path ?? Path.GetFileNameWithoutExtension(session.Title),
            Filter = "BRF 점자문서|*.brf|원문 텍스트|*.txt|점자 텍스트 (판면 포함)|*.txt", DefaultExt = ".brf", FilterIndex = format };
        fileBusy = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime.Token);
        var ct = cancellation.Token;
        try
        {
            var owner = session;
            var snapshot = SmjFile.Snapshot(owner).Manifest;
            var invalid = owner.Blocks.Where(b => b.IsBrailleLocked || b.Braille.Length == 0 && b.Source.Length > 0 && !LocalBrailleEngine.IsLayoutMarker(b.Source) && !LocalBrailleEngine.IsNonPrintingTag(b.Source)
                || !b.HasManualBraille && translatedRevisions.GetValueOrDefault(b, -1) != b.SourceRevision).Select(b => b.Id).ToHashSet();
            var initialFormat = dialog.FilterIndex == 1 ? ".brf" : dialog.FilterIndex == 2 ? ".txt — 원문" : ".txt — 점자";
            var local = engine as LocalBrailleEngine;
            string? fullBrf = null;
            async Task<string> BuildAsync(SmjManifest selected)
            {
                DocumentExport.RequireCompletedPages(selected);
                if (selected.Blocks.Any(b => invalid.Contains(b.Id)))
                    throw new InvalidOperationException("선택한 범위에 점역하지 못했거나 원문·옵션 변경 후 점역이 필요한 블록이 있습니다. 재점역 후 내보내 주세요.");
                if (local is null) throw new InvalidOperationException("샘플 엔진에서는 점자 문서를 내보낼 수 없습니다.");
                var result = await local.ComposeAsync(selected, ct);
                if (result.Replaced.Count > 0 && !ConfirmExport("BRF에서 빈칸으로 바뀌는 문자가 있습니다.",
                    "바뀌는 문자: " + string.Join(", ", result.Replaced.Select(p => $"{p.Key}({p.Value})")) + ". 계속 내보내시겠습니까?")) throw new OperationCanceledException("내보내기를 취소했습니다.");
                if (result.SpacingUnknownBlockIds is { Length: > 0 } ids && !ConfirmExport("조판 빈 줄을 확인할 수 없는 블록이 있습니다.",
                    "블록 " + DescribeSpacingBlocks(ids) + ". 현재 엔진에서 확인 정보를 제공하지 않거나 점자를 직접 수정한 블록입니다. 저장된 점자는 그대로 유지됩니다. 현재 판면으로 내보내시겠습니까?", height: 226))
                    throw new OperationCanceledException("내보내기를 취소했습니다.");
                if (result.AlignmentUnknownBlockIds is { Length: > 0 } alignmentIds && !ConfirmExport("정렬 태그와 점자 줄의 대응을 확인할 수 없습니다.",
                    "블록 " + DescribeSpacingBlocks(alignmentIds) + ". 해당 줄의 정렬을 적용하지 않은 현재 판면으로 내보내시겠습니까?"))
                    throw new OperationCanceledException("내보내기를 취소했습니다.");
                return result.Brf;
            }
            async Task<int> CountAsync()
            {
                fullBrf ??= await BuildAsync(snapshot);
                return DocumentExport.BrfPages(fullBrf).Length;
            }
            var ranges = new ExportRangeDialog(DocumentExport.OriginalNumbers(snapshot), CountAsync, format: initialFormat, allowFormatChange: true);
            if (ExportRangeSelection is null) ranges.Owner = this;
            if (chooseRange && !(ExportRangeSelection?.Invoke(ranges) ?? ranges.ShowDialog() == true)) return;
            var selectedFormat = ranges.SelectedFormat;
            var plainText = selectedFormat == ".txt — 원문";
            var brailleBrf = selectedFormat == ".brf";
            // 범위 창에서 「저장」을 누른 뒤에 저장 위치를 고릅니다.
            if (path is null)
            {
                dialog.FilterIndex = brailleBrf ? 1 : plainText ? 2 : 3; dialog.DefaultExt = brailleBrf ? ".brf" : ".txt";
                if (dialog.ShowDialog(this) != true) return;
            }
            var targetPath = Path.ChangeExtension(dialog.FileName, brailleBrf ? ".brf" : ".txt");
            if (!string.Equals(targetPath, dialog.FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath)
                && !ConfirmExport("형식 변경으로 선택한 파일이 이미 있습니다.", targetPath + "\n덮어쓰시겠습니까?", "덮어쓰기", 226)) return;
            var range = ranges.Result;
            var selected = DocumentExport.SelectOriginal(snapshot, range);
            DocumentExport.RequireCompletedPages(selected);
            string text;
            if (plainText) text = string.Join("\r\n", selected.Blocks.Select(b => b.Source));
            else
            {
                if (selected.Blocks.Any(b => b.Translation?.NeedsReview == true)
                    && !ConfirmExport("선택한 범위에 누락 문자 또는 점역 불가 표시가 있습니다.", "현재 점자 그대로 내보내시겠습니까?")) return;
                var brf = range.Scope == ExportScope.OriginalPages ? await BuildAsync(selected) : fullBrf ?? await BuildAsync(snapshot);
                brf = DocumentExport.SelectBraille(brf, range);
                text = brailleBrf ? brf : DocumentExport.BrailleText(await local!.ReadBrfAsync(brf, ct));
            }
            ct.ThrowIfCancellationRequested();
            var temp = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), ct);
                ct.ThrowIfCancellationRequested();
                File.Move(temp, targetPath, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            if (session == owner) session.Status = "선택한 범위를 내보냈습니다. 작업 파일 저장은 Ctrl+S를 눌러 주세요.";
        }
        finally { cancellation.Cancel(); fileBusy = false; }
    }
}
