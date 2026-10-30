using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Services;
using SharpVectors.Converters;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private sealed class OpenDocument(DocumentSession model, EditorDocuments buffers)
    {
        public DocumentSession Model { get; } = model;
        public EditorDocuments Buffers { get; } = buffers;
        public string? Path;
        public WorkFileLease? Lease;
        public CancellationTokenSource JobLifetime { get; } = new();
        public bool ShowOriginal;
        public bool ShowSource = true;
        public bool ShowBraille = true;
        public bool ShowUpload;
        public bool IsStartupPlaceholder;
        public long PlaceholderVersion;
        public int PageSyncInProgress;
        public bool ServerSaveInProgress;
        public bool OriginalKindKnown;
        public string[]? UploadPaths;
        public Dictionary<EditorBlock, int> Translated = [];
        public TabItem Tab { get; } = new();
        public PropertyChangedEventHandler? LabelListener;
        public TabConversionProgress? ProgressIndicator;
    }
    private readonly List<OpenDocument> openDocuments = [];
    // A closed tab may still represent an accepted or ambiguous server restart. Keep its
    // guard until that exact locally saved recovery document is reopened and resolved.
    private sealed class ClosedJobRecovery(OpenDocument context)
    {
        public OpenDocument Context = context;
    }
    private readonly List<ClosedJobRecovery> closedJobRecoveries = [];

    private void RetainClosedJobRecovery(OpenDocument context)
    {
        closedJobRecoveries.RemoveAll(g => ReferenceEquals(g.Context.Model, context.Model));
        if (context.Model.JobId is not null && (context.Model.HasPendingRestart || context.Model.HasPendingServerPages))
            closedJobRecoveries.Add(new(context));
    }

    private void BindClosedJobRecovery(OpenDocument reopened)
    {
        if (reopened.Path is null || reopened.Model.JobId is null) return;
        foreach (var guard in closedJobRecoveries)
        {
            var old = guard.Context;
            if (old.PageSyncInProgress != 0 || old.Path is null || old.Model.JobId != reopened.Model.JobId
                || !LocalWorkspace.SamePath(old.Path, reopened.Path)) continue;
            // Do not release the guard using an older, replaced, or unrelated on-disk copy.
            var before = System.Text.Json.JsonSerializer.Serialize(new { old.Model.PendingRestart, Pages = old.Model.PendingServerPages.OrderBy(p => p.Key).ToArray() });
            var after = System.Text.Json.JsonSerializer.Serialize(new { reopened.Model.PendingRestart, Pages = reopened.Model.PendingServerPages.OrderBy(p => p.Key).ToArray() });
            if (before == after) guard.Context = reopened;
        }
    }

    private bool HasClosedJobRecovery(OpenDocument context)
    {
        BindClosedJobRecovery(context);
        closedJobRecoveries.RemoveAll(g => openDocuments.Contains(g.Context) && g.Context.PageSyncInProgress == 0
            && !g.Context.Model.HasPendingRestart && !g.Context.Model.HasPendingServerPages);
        return context.Model.JobId is { } id && closedJobRecoveries.Any(g => g.Context.Model.JobId == id
            && !ReferenceEquals(g.Context.Model, context.Model));
    }
    private OpenDocument? openDocument;
    private bool tabSwitching;

    private void StoreActiveDocument()
    {
        if (openDocument is null) return;
        openDocument.Path = filePath;
        openDocument.ShowOriginal = OriginalToggle.IsChecked;
        openDocument.ShowSource = SourceToggle.IsChecked;
        openDocument.ShowBraille = BrailleToggle.IsChecked;
        openDocument.Translated = new(translatedRevisions);
    }

    private static bool IsUntouchedPlaceholder(OpenDocument? context) => context is { IsStartupPlaceholder: true, Path: null }
        && !context.Model.IsDirty && context.Model.ChangeVersion == context.PlaceholderVersion && context.Model.JobId is null
        && context.Model.OriginalFiles.Count == 0 && context.Model.OriginalPages.Count == 0
        && context.Model.Blocks.Count == 1 && context.Model.Blocks[0] is { Source.Length: 0, Braille.Length: 0, HasManualBraille: false }
        && context.Model.Blocks[0].Memos.Count == 0;

    private OpenDocument RegisterDocument(DocumentSession model, string? path, bool showOriginal, bool showUpload)
    {
        var existing = openDocuments.FirstOrDefault(t => ReferenceEquals(t.Model, model));
        if (existing is not null) return existing;
        var context = new OpenDocument(model, new()) { Path = path, ShowOriginal = showOriginal, ShowUpload = showUpload };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Margin = new(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var closeIcon = new SvgViewbox { Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false };
        var close = new Button
        {
            Style = (Style)FindResource("PanelButton"), MinHeight = 20, MinWidth = 20, Height = 20, Width = 20,
            Padding = new(0), Margin = new(2, 0, 0, 0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            ToolTip = "문서 탭 닫기", Content = closeIcon
        };
        AutomationProperties.SetAutomationId(close, "DocumentTabClose");
        void UpdateLabel()
        {
            label.Text = (model.IsReadOnly ? "[읽기 전용] " : "") + (model.IsDirty ? "● " : "") + model.Title;
            context.Tab.ToolTip = context.Path ?? model.Title;
            AutomationProperties.SetName(close, $"{model.Title} 탭 닫기");
        }
        context.ProgressIndicator = new TabConversionProgress(model, () => CancelTabJobAsync(context));
        context.LabelListener = (_, e) =>
        {
            if (e.PropertyName is nameof(DocumentSession.Title) or nameof(DocumentSession.IsDirty) or nameof(DocumentSession.IsReadOnly)) UpdateLabel();
            if (e.PropertyName is nameof(DocumentSession.Title) or nameof(DocumentSession.Progress)) context.ProgressIndicator.Refresh();
        };
        model.PropertyChanged += context.LabelListener;
        close.Click += async (_, e) => { e.Handled = true; await CloseDocumentAsync(context); };
        header.Children.Add(label); header.Children.Add(context.ProgressIndicator); header.Children.Add(close); context.Tab.Header = header; context.Tab.Tag = context; UpdateLabel();
        tabSwitching = true;
        try { openDocuments.Add(context); DocumentTabs.Items.Add(context.Tab); }
        finally { tabSwitching = false; }
        // SharpVectors can pump layout while loading SVGs. Wait until the document
        // switch has completed before touching the icon, to avoid rendering the
        // editor against mismatched old/new document buffers.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (openDocuments.Contains(context)) closeIcon.Source = new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/tab-close.svg");
        }));
        return context;
    }

    private async Task CancelTabJobAsync(OpenDocument context)
    {
        var model = context.Model;
        if (model.JobId is not { } id || model.Progress is null) return;
        try
        {
            var cancellation = await jobs.CancelAsync(id, context.JobLifetime.Token);
            if (context.JobLifetime.IsCancellationRequested || model.JobId != id) return;
            var finished = cancellation.IsTerminal || model.JobStatus is "COMPLETED" or "FAILED" or "CANCELLED" or "CANCELED";
            model.CancellationRequested = cancellation.Accepted && !finished;
            model.ErrorNotice = model.Status = cancellation.Notice(finished);
        }
        catch (OperationCanceledException) when (context.JobLifetime.IsCancellationRequested) { }
        catch (Exception ex) { model.ErrorNotice = model.Status = "변환 중단 요청을 확인하지 못했습니다. 기존 작업은 유지됩니다: " + ex.Message; }
    }

    private void DocumentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (tabSwitching || e.Source != DocumentTabs || DocumentTabs.SelectedItem is not TabItem { Tag: OpenDocument chosen } || chosen == openDocument) return;
        if (fileBusy)
        { tabSwitching = true; DocumentTabs.SelectedItem = openDocument?.Tab; tabSwitching = false; return; }
        SetSession(chosen.Model, chosen.Path, chosen.ShowOriginal);
    }

    private async Task CloseDocumentAsync(OpenDocument context)
    {
        if (fileBusy) return;
        SetSession(context.Model, context.Path, context.ShowOriginal);
        if (!await ConfirmDiscardAsync()) return;
        var next = openDocuments.FirstOrDefault(t => t != context);
        RetainClosedJobRecovery(context);
        RemoveDocument(context);
        if (next is null)
        {
            SetSession(BlankDocument(), null, true, showUpload: true);
            openDocument!.IsStartupPlaceholder = true; openDocument.PlaceholderVersion = session.ChangeVersion;
        }
        else SetSession(next.Model, next.Path, next.ShowOriginal);
    }

    private void RemoveDocument(OpenDocument context)
    {
        tabSwitching = true;
        try
        {
            context.JobLifetime.Cancel(); context.Lease?.Dispose();
            context.Model.PropertyChanged -= context.LabelListener; context.ProgressIndicator?.Dispose();
            DocumentTabs.Items.Remove(context.Tab); openDocuments.Remove(context); context.Buffers.Dispose();
            if (context == openDocument) openDocument = null;
        }
        finally { tabSwitching = false; }
    }
    private async Task<bool> ConfirmAllDocumentsAsync()
    {
        var previous = openDocument;
        foreach (var context in openDocuments.ToArray())
        {
            SetSession(context.Model, context.Path, context.ShowOriginal);
            if (!await ConfirmDiscardAsync()) return false;
        }
        if (previous is not null) SetSession(previous.Model, previous.Path, previous.ShowOriginal);
        return true;
    }
}
