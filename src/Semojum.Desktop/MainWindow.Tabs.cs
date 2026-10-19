using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private sealed class OpenDocument(DocumentSession model, EditorDocuments buffers)
    {
        public DocumentSession Model { get; } = model;
        public EditorDocuments Buffers { get; } = buffers;
        public string? Path;
        public CancellationTokenSource JobLifetime { get; } = new();
        public bool ShowOriginal;
        public Dictionary<EditorBlock, int> Translated = [];
        public TabItem Tab { get; } = new();
        public PropertyChangedEventHandler? LabelListener;
    }
    private readonly List<OpenDocument> openDocuments = [];
    private OpenDocument? openDocument;
    private bool tabSwitching;

    private void StoreActiveDocument()
    {
        if (openDocument is null) return;
        openDocument.Path = filePath;
        openDocument.Translated = new(translatedRevisions);
    }

    private OpenDocument RegisterDocument(DocumentSession model, string? path, bool showOriginal)
    {
        var existing = openDocuments.FirstOrDefault(t => ReferenceEquals(t.Model, model));
        if (existing is not null) return existing;
        var context = new OpenDocument(model, new()) { Path = path, ShowOriginal = showOriginal };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Margin = new(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        void UpdateLabel() { label.Text = (model.IsDirty ? "● " : "") + model.Title; context.Tab.ToolTip = context.Path ?? model.Title; }
        context.LabelListener = (_, e) => { if (e.PropertyName is nameof(DocumentSession.Title) or nameof(DocumentSession.IsDirty)) UpdateLabel(); };
        model.PropertyChanged += context.LabelListener;
        var close = new Button { Content = "×", MinHeight = 20, Height = 20, Width = 22, Padding = new(0), Margin = new(2, 0, 0, 0) };
        close.Click += async (_, e) => { e.Handled = true; await CloseDocumentAsync(context); };
        header.Children.Add(label); header.Children.Add(close); context.Tab.Header = header; context.Tab.Tag = context; UpdateLabel();
        tabSwitching = true;
        try { openDocuments.Add(context); DocumentTabs.Items.Add(context.Tab); }
        finally { tabSwitching = false; }
        return context;
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
        RemoveDocument(context);
        if (next is null) SetSession(BlankDocument(), null, false);
        else SetSession(next.Model, next.Path, next.ShowOriginal);
    }

    private void RemoveDocument(OpenDocument context)
    {
        tabSwitching = true;
        try
        {
            context.JobLifetime.Cancel();
            context.Model.PropertyChanged -= context.LabelListener;
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
