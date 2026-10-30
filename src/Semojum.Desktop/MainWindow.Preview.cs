using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private long previewGeneration;
    private CancellationTokenSource? debounce;
    private bool pendingSourceTranslation;
    private int currentPreviewLine;
    private string currentPreviewCells = "";

    private async void QueuePreview(bool sourceChanged)
    {
        if (resourcesReleased || smoke || switching || session?.IsReadOnly == true || session?.ActiveBlock is not { } block || engine is not LocalBrailleEngine local) return;
        pendingSourceTranslation |= sourceChanged;
        var generation = ++previewGeneration;
        debounce?.Cancel(); debounce?.Dispose(); debounce = new();
        var waitToken = debounce.Token; var owner = session; var ct = documentLifetime.Token;
        try
        {
            await Task.Delay(450, waitToken);
            if (resourcesReleased || ct.IsCancellationRequested || generation != previewGeneration || session != owner || session.ActiveBlock != block) return;
            var translate = pendingSourceTranslation; pendingSourceTranslation = false;
            if (translate && autoTranslate && translatedRevisions.GetValueOrDefault(block, -1) != block.SourceRevision)
                await TranslateAsync(block, ChooseTranslation(block), interactive: false);
            if (generation != previewGeneration || !(SourceEditor.IsKeyboardFocusWithin || PageEditor.Editor.IsKeyboardFocusWithin) || LinePreviewToggle.IsChecked != true) return;
            var source = PageEditor.Editor.IsKeyboardFocusWithin ? PageEditor.Editor : SourceEditor;
            var line = source.Document.GetLineByNumber(source.TextArea.Caret.Line);
            var result = await local.TranslateElementAsync(source.Document.GetText(line), new(), session.Options, ct);
            if (generation == previewGeneration && session == owner)
            {
                currentPreviewLine = line.LineNumber; currentPreviewCells = result.Cells;
                LineStrip.SetPreview(line.LineNumber, result.Cells);
                detachedPane?.SetLinePreview(line.LineNumber, result.Cells);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SidecarException or TimeoutException or System.ComponentModel.Win32Exception)
        { if (generation == previewGeneration) session.Status = "줄번역을 표시하지 못했습니다. 엔진 연결을 확인해 주세요."; }
    }

    private void LinePreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        SetLinePreviewVisible(LinePreviewToggle.IsChecked);
    }

    private void SetLinePreviewVisible(bool visible)
    {
        LinePreviewToggle.IsChecked = visible;
        LineStrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        detachedPane?.SetLinePreviewVisible(visible);
        QueuePreview(false);
    }

    private async void OriginalImage_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var point = e.GetPosition(OriginalImageGrid);
        var selected = session.Blocks.Where(b => b.Page == session.ActiveBlock?.Page && b.Metadata.BoundingBox is { Length: 4 } r
            && point.X >= r[0] && point.X <= r[2] && point.Y >= r[1] && point.Y <= r[3])
            .OrderBy(b => (b.Metadata.BoundingBox![2] - b.Metadata.BoundingBox[0]) * (b.Metadata.BoundingBox[3] - b.Metadata.BoundingBox[1])).FirstOrDefault();
        if (selected is null) return;
        try { await NavigateToAsync(new(selected, SearchPanel.Text, selected.SourceCaret)); }
        catch (Exception ex) { ShowError(ex); }
    }
    private long originalGeneration;
    private async void ShowOriginal(int page)
    {
        if (smoke) return;
        var generation = ++originalGeneration; var owner = session;
        OriginalEmpty.Visibility = Visibility.Visible;
        OriginalImage.Source = null; OriginalText.Text = "원본 미리보기 없음";
        OriginalSelection.Visibility = Visibility.Collapsed;
        if (!session.OriginalPages.TryGetValue(page, out var original)) return;
        if (original.Type == "text") { OriginalText.Text = string.Join("\n", original.Lines ?? []); FinishOriginalWheelTransition(owner, page, OriginalScroll); return; }
        if (original.AssetName is null || !session.OriginalFiles.TryGetValue(original.AssetName, out var bytes)) return;
        try
        {
            BitmapSource image;
            if (original.Type == "pdf") { OriginalText.Text = "PDF 원본을 표시하는 중…"; image = await PdfPreview.RenderAsync(bytes, page, documentLifetime.Token); }
            else
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1800; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
            }
            if (generation != originalGeneration || owner != session) return;
            OriginalImage.Source = image; OriginalText.Text = $"원본 {page}쪽";
            var width = original.Width > 0 ? original.Width : image.PixelWidth;
            var height = original.Height > 0 ? original.Height : image.PixelHeight;
            OriginalImageGrid.Width = width; OriginalImageGrid.Height = height;
            if (session.ActiveBlock?.Metadata.BoundingBox is { Length: 4 } box && box.All(double.IsFinite))
            {
                var x = Math.Clamp(box[0], 0, width); var y = Math.Clamp(box[1], 0, height);
                OriginalSelection.Width = Math.Max(0, Math.Clamp(box[2], 0, width) - x);
                OriginalSelection.Height = Math.Max(0, Math.Clamp(box[3], 0, height) - y);
                System.Windows.Controls.Canvas.SetLeft(OriginalSelection, x); System.Windows.Controls.Canvas.SetTop(OriginalSelection, y);
                OriginalSelection.Visibility = Visibility.Visible;
            }
            FinishOriginalWheelTransition(owner, page, OriginalScroll);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        { if (generation == originalGeneration && owner == session) { OriginalText.Text = "원본을 표시할 수 없습니다: " + ex.Message; FinishOriginalWheelTransition(owner, page, OriginalScroll); } }
    }
}
