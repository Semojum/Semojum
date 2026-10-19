using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Dialogs;

public sealed class BraillePreviewDialog : CompactDialog
{
    private readonly TextEditor editor = new() { IsReadOnly = true, ShowLineNumbers = true, FontFamily = new("Segoe UI Symbol"), FontSize = 22, WordWrap = false, Padding = new(12), Background = Brushes.White };
    private readonly TextBox number = new() { Width = 72, Text = "1", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock caption = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) };
    public int Page { get; private set; }
    public BraillePreviewDialog(string[][] pages, string brf, PageLayout layout, string title, int initial = 1) : base("점자 면 미리보기 — " + title, 850, 880)
    {
        void Show(int page)
        {
            if (page < 1 || page > pages.Length) throw new ArgumentException($"1–{pages.Length}면 안에서 입력해 주세요.");
            Page = page; number.Text = page.ToString(); editor.Text = string.Join("\n", pages[page - 1]);
            caption.Text = $"{page} / {pages.Length}면 · 번호 {layout.StartPage + page - 1} · {layout.Columns}칸 × {layout.Lines}줄";
        }
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12) };
        toolbar.Children.Add(Button("이전", () => { if (Page > 1) Show(Page - 1); return Task.CompletedTask; }));
        toolbar.Children.Add(number); toolbar.Children.Add(Button("이동", () => RunAsync(() => { Show(DocumentNavigation.Number(number.Text, Page)); return Task.CompletedTask; })));
        toolbar.Children.Add(Button("다음", () => { if (Page < pages.Length) Show(Page + 1); return Task.CompletedTask; })); toolbar.Children.Add(caption);
        DockPanel.SetDock(toolbar, Dock.Top); Root.Children.Add(toolbar);
        var hint = new TextBlock { Text = "꼬리말·쪽번호·빈 면까지 BRF 파일과 같은 결과입니다. 내용을 고치려면 편집 화면으로 돌아가세요.", TextWrapping = TextWrapping.Wrap, Margin = new(16, 4, 16, 8) };
        DockPanel.SetDock(hint, Dock.Top); Root.Children.Add(hint);
        editor.Options.ShowColumnRuler = true; editor.Options.ColumnRulerPosition = layout.Columns;
        editor.TextArea.TextView.ColumnRulerPen = new Pen(Brushes.Gray, 1);
        var paper = new Border { Margin = new(24, 8, 24, 20), BorderBrush = Brushes.Gray, BorderThickness = new(1), Child = editor };
        Body(paper);
        async Task Save(bool txt)
        {
            var range = new ExportRangeDialog([], () => Task.FromResult(pages.Length), brailleOnly: true) { Owner = this };
            if (range.ShowDialog() != true) return;
            var selected = DocumentExport.SelectBraille(brf, range.Result);
            var dialog = new SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(title), Filter = txt ? "점자 텍스트|*.txt" : "BRF|*.brf" };
            if (dialog.ShowDialog(this) != true) return;
            var output = txt ? DocumentExport.BrailleText(range.Result.Scope == ExportScope.All ? pages : pages.Skip(range.Result.Start - 1).Take(range.Result.End - range.Result.Start + 1)) : selected;
            var temp = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temp, output, new UTF8Encoding(false)); File.Move(temp, dialog.FileName, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        Action("BRF 저장", () => Save(false)); Action("점자 TXT 저장", () => Save(true));
        Action("점자 인쇄…", async () =>
        {
            var range = new ExportRangeDialog([], () => Task.FromResult(pages.Length), brailleOnly: true) { Owner = this };
            if (range.ShowDialog() != true) return;
            await RawBraillePrinter.PrintAsync(this, DocumentExport.SelectBraille(brf, range.Result), title);
        });
        Action("닫기", Close, true); Show(initial);
    }
}
