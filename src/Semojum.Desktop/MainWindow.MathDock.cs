using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private DocumentSession? mathPreviewSession;
    private EditorBlock? mathPreviewBlock, dismissedMathPreview;
    private int dismissedMathStart = -1;
    private bool mathPreviewQueued;
    internal Func<string, BlockMetadata, BrailleOptions, CancellationToken, Task<BrailleTranslation>>? InlineMathTranslationForChecks { get; set; }

    private void InitializeMathPreview()
    {
        DataContextChanged += (_, _) => ObserveMathSession();
        MathDock.CloseRequested += () => { dismissedMathPreview = mathPreviewBlock; dismissedMathStart = mathPreviewBlock?.Metadata.Type == "formula" ? -1 : mathPreviewBlock is { } b ? SelectedInlineFormula(b).Start : -1; UpdateMathPreview(); };
        MathDock.EditRequested += async () => { if (!accountStarting && !resourcesReleased) await EditMathAsync(); };
        DraftDock.IsVisibleChanged += (_, _) => UpdateMathPreview();
        SourcePanel.SizeChanged += (_, _) => ResizeMathPreview();
        PageEditor.Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateMathPreview();
        PageEditor.Editor.TextArea.SelectionChanged += (_, _) => UpdateMathPreview();
        SourceEditor.TextArea.Caret.PositionChanged += (_, _) => UpdateMathPreview();
        SourceEditor.TextArea.SelectionChanged += (_, _) => UpdateMathPreview();
        ObserveMathSession(); ResizeMathPreview();
    }

    private void ObserveMathSession()
    {
        if (mathPreviewSession is not null) mathPreviewSession.PropertyChanged -= MathSessionChanged;
        mathPreviewSession = session; dismissedMathPreview = null;
        if (mathPreviewSession is not null) mathPreviewSession.PropertyChanged += MathSessionChanged;
        ObserveMathBlock();
    }
    private void MathSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.ActiveBlock)) ObserveMathBlock();
        else if (e.PropertyName == nameof(DocumentSession.IsReadOnly)) UpdateMathPreview();
    }
    private void ObserveMathBlock()
    {
        if (mathPreviewBlock is not null) mathPreviewBlock.PropertyChanged -= MathBlockChanged;
        var next = mathPreviewSession?.ActiveBlock;
        if (!ReferenceEquals(mathPreviewBlock, next)) dismissedMathPreview = null;
        mathPreviewBlock = next;
        if (mathPreviewBlock is not null) mathPreviewBlock.PropertyChanged += MathBlockChanged;
        UpdateMathPreview();
    }
    private void MathBlockChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(EditorBlock.Source) or nameof(EditorBlock.Metadata) or nameof(EditorBlock.IsBrailleLocked)) UpdateMathPreview(); }
    private void UpdateMathPreview()
    {
        if (MathDock is null) return;
        var block = mathPreviewBlock;
        if (block is null || DraftDock.Visibility == Visibility.Visible || SourcePanel.ActualHeight < 140)
        { MathDock.Visibility = Visibility.Collapsed; return; }
        var selection = block.Metadata.Type == "formula" ? null : SelectedInlineFormula(block);
        if (selection is { Status: FormulaRangeStatus.None }) { MathDock.Visibility = Visibility.Collapsed; return; }
        var selectedStart = selection?.Start ?? -1;
        if (ReferenceEquals(block, dismissedMathPreview) && selectedStart == dismissedMathStart)
        { MathDock.Visibility = Visibility.Collapsed; return; }
        if (ReferenceEquals(block, dismissedMathPreview) && selectedStart != dismissedMathStart) dismissedMathPreview = null;
        if (selection is { Status: FormulaRangeStatus.Malformed })
        { MathDock.ShowInvalidMarkup(); MathDock.Visibility = Visibility.Visible; return; }
        if (selection is null && (block.Source.Contains("<!수식", StringComparison.Ordinal) || block.Source.Contains("<!/수식", StringComparison.Ordinal)))
        {
            var full = FormulaRanges.Find(block.Source, 0, block.Source.Length);
            if (full.Status != FormulaRangeStatus.Selected || full.Start != 0 || full.Length != block.Source.Length)
            { MathDock.ShowInvalidMarkup(); MathDock.Visibility = Visibility.Visible; return; }
        }
        const string start = "<!수식>", end = "<!/수식>";
        var value = selection?.Latex ?? block.Source;
        if (selection is null && value.StartsWith(start, StringComparison.Ordinal) && value.EndsWith(end, StringComparison.Ordinal))
            value = value[start.Length..^end.Length];
        MathDock.ShowFormula(value, mathPreviewSession?.IsReadOnly == false && !block.Metadata.IsBlocked && !block.IsBrailleLocked);
        MathDock.Visibility = Visibility.Visible;
    }
    private FormulaRange SelectedInlineFormula(EditorBlock block)
    {
        if (PageEditor.Visibility == Visibility.Visible)
        {
            if (PageEditor.IsSynchronizing) { QueueMathPreview(); return new(FormulaRangeStatus.None); }
            try
            {
                var spans = PageEditor.Selection;
                if (spans.Length != 1 || !ReferenceEquals(spans[0].Block, block)) return new(FormulaRangeStatus.None);
                return FormulaRanges.Find(block.Source, spans[0].Start, spans[0].Length);
            }
            catch (ArgumentOutOfRangeException) { QueueMathPreview(); return new(FormulaRangeStatus.None); }
        }
        return FormulaRanges.Find(block.Source, SourceEditor.SelectionStart, SourceEditor.SelectionLength);
    }
    private void QueueMathPreview()
    {
        if (mathPreviewQueued || resourcesReleased) return;
        mathPreviewQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { mathPreviewQueued = false; if (!resourcesReleased) UpdateMathPreview(); }));
    }
    private void ResizeMathPreview()
    {
        MathDock.MaxHeight = Math.Max(28, SourcePanel.ActualHeight - 102);
        UpdateMathPreview();
    }
}
