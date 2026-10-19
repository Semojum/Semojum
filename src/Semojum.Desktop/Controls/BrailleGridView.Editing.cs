using System.Windows;
using System.Windows.Input;
using Semojum.Core;

namespace Semojum.Desktop.Controls;

public sealed partial class BrailleGridView
{
    public IReadOnlyList<EditorBlock> Blocks { get; set; } = [];
    public bool IsReadOnly { get; set; }
    public bool CoordinatesCurrent { get; set; } = true;
    public Func<BlockSpan[], string, Task>? ReplaceAsync { get; set; }
    public Action<EditorBlock, EditorBlock>? MoveBlock { get; set; }
    public Action<bool>? ReplayHistory { get; set; }
    public Action<string>? Notify { get; set; }
    private GridCell? anchor, caret;
    private BlockSpan[] drawSelection = [];
    private readonly SixDotInput input = new();
    public bool SixDotEnabled { get; set; }
    public bool SpaceEnabled { get; set; } = true;
    public bool EnterEnabled { get; set; } = true;
    public void ConfigureKeys(string keys) => input.Configure(keys);
    private bool dragSelecting, editing, selectedBlock;
    public BlockSpan[] Selection
    {
        get
        {
            if (caret is null) return [];
            var page = Blocks.Where(b => b.Page == caret.Block.Page).ToArray();
            if (!page.Contains(caret.Block) || anchor is null || !page.Contains(anchor.Block)) return [];
            int Position(GridCell cell) => page.TakeWhile(b => b != cell.Block).Sum(b => b.Braille.Length + 1) + Math.Clamp(cell.Offset, 0, cell.Block.Braille.Length);
            var a = Position(anchor); var b = Position(caret);
            return BlockRange.Select(page, SearchPanel.Braille, Math.Min(a, b), Math.Abs(b - a));
        }
    }
    public bool HasSelection => drawSelection.Any(s => s.Length > 0) || Selection.Length > 1;
    private GridCell[] CurrentRow() => !CoordinatesCurrent || caret is null ? [] :
        Items.Cast<BrailleGridFace>().SelectMany(f => f.Rows).FirstOrDefault(r => r.Any(c => c?.Block == caret.Block && c.Offset == caret.Offset))?.OfType<GridCell>().ToArray() ?? [];
    public async Task ClearLineAsync()
    {
        var row = CurrentRow(); if (row.Length == 0) return;
        var last = row.LastOrDefault(c => !c.IsInsertion);
        if (last is null) return;
        Point(row[0], false); Point(new(last.Block, last.Offset + 1, '⠀', true), true);
        await InsertAsync("");
    }
    private bool IsSelected(GridCell cell) => drawSelection.Any(s => s.Block == cell.Block && cell.Offset >= s.Start && cell.Offset < s.Start + s.Length);
    internal void Point(GridCell cell, bool extend)
    {
        if (extend && anchor is not null && anchor.Block.Page != cell.Block.Page) return;
        if (!extend || anchor is null) anchor = cell;
        caret = cell; ActiveBlock = cell.Block; CaretOffset = cell.Offset;
        CellSelected?.Invoke(cell); InvalidateFaces();
    }
    public void ResetSelection()
    { anchor = caret = null; selectedBlock = false; InvalidateFaces(); }
    public Task InsertAsync(string text) => InsertCoreAsync(text, false);
    public Task InsertPageBreakAsync() => InsertCoreAsync("\n<!쪽바꿈>\n", true);
    private async Task InsertCoreAsync(string text, bool layoutMarker)
    {
        if (editing || IsReadOnly || ReplaceAsync is null || caret is null) return;
        if (!DocumentSearch.IsBrailleText(layoutMarker ? BrailleMarkup.VisibleText(text) : text)) { Notify?.Invoke("점자와 공백만 입력할 수 있습니다."); return; }
        var spans = Selection;
        if (spans.Length == 0 || spans.Any(s => s.Block.IsBrailleLocked || s.Block.Metadata.IsBlocked)) return;
        editing = true;
        try
        {
            var first = spans[0];
            if (!HasSelection && text.Length > 0) text = caret.Prefix + text;
            await ReplaceAsync(spans, text);
            var point = new GridCell(first.Block, Math.Min(first.Start + text.Length, first.Block.Braille.Length), '⠀', true);
            anchor = caret = point; ActiveBlock = point.Block; CaretOffset = point.Offset;
            CellSelected?.Invoke(point); InvalidateFaces();
        }
        catch (Exception ex) { Notify?.Invoke(ex.Message); }
        finally { editing = false; }
    }
    public bool Copy(bool cut)
    {
        if (!HasSelection) return false;
        Clipboard.SetText(string.Join("\n", Selection.Select(s => BrailleMarkup.VisibleText(s.Block.Braille.Substring(s.Start, s.Length)))));
        if (cut) _ = InsertAsync("");
        return true;
    }
    private void Move(int direction, bool extend, bool vertical = false)
    {
        if (caret is null) return;
        if (vertical && !CoordinatesCurrent) return;
        if (vertical)
        {
            var lines = Items.Cast<BrailleGridFace>().SelectMany(f => f.Rows).ToArray();
            var row = Array.FindIndex(lines, r => r.Any(c => c?.Block == caret.Block && c.Offset == caret.Offset));
            if (row >= 0 && row + direction >= 0 && row + direction < lines.Length)
            {
                var column = Array.FindIndex(lines[row], c => c?.Block == caret.Block && c.Offset == caret.Offset);
                var target = lines[row + direction].Where(c => c is not null).OrderBy(c => Math.Abs(Array.IndexOf(lines[row + direction], c) - column)).FirstOrDefault();
                if (target is not null) Point(target, extend);
            }
            return;
        }
        var offset = caret.Offset + direction; var block = caret.Block;
        var page = Blocks.Where(b => b.Page == block.Page).ToArray(); var index = Array.IndexOf(page, block);
        if (offset < 0 && index > 0) { block = page[index - 1]; offset = block.Braille.Length; }
        else if (offset > block.Braille.Length && index + 1 < page.Length) { block = page[index + 1]; offset = 0; }
        Point(new(block, BrailleMarkup.MovePastMarker(block.Braille, offset, direction), '⠀', true), extend);
    }
    public string DescribeCaret()
    {
        var faces = Items.Cast<BrailleGridFace>().ToArray();
        foreach (var f in faces)
            for (var y = 0; y < f.Rows.Length; y++)
                for (var x = 0; x < f.Rows[y].Length; x++)
                    if (f.Rows[y][x] is { } c && c.Block == ActiveBlock && c.Offset == CaretOffset)
                        return $"{f.Number}면 · {y + 1}줄 {x + 1}칸 · 총 {faces.Length}면";
        return $"총 {faces.Length}면";
    }
    public bool GoToFace(int number)
    {
        if (number < 1 || number > Items.Count) return false;
        var face = (BrailleGridFace)Items[number - 1];
        ScrollIntoView(face);
        if (face.Rows.SelectMany(r => r).FirstOrDefault(c => c is not null) is { } cell) Point(cell, false);
        Focus(); return true;
    }
    private void InstallEditing()
    {
        Focusable = true;
        PreviewTextInput += async (_, e) =>
        { e.Handled = true; if (Keyboard.Modifiers == ModifierKeys.None) await InsertAsync(e.Text); };
        PreviewKeyDown += async (_, e) =>
        {
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (ctrl && e.Key is Key.Z or Key.Y) { e.Handled = true; ReplayHistory?.Invoke(e.Key == Key.Y); ResetSelection(); return; }
            if (caret is null) return;
            if (SixDotEnabled && Keyboard.Modifiers == ModifierKeys.None)
            {
                var key = e.Key.ToString();
                if (key.Length == 1 && input.Press(key[0])) { e.Handled = true; return; }
                if (e.Key == Key.Space && (!SpaceEnabled || e.IsRepeat) || e.Key == Key.Enter && !EnterEnabled) { e.Handled = true; return; }
            }
            if (ctrl && e.Key == Key.A)
            {
                e.Handled = true;
                var blocks = selectedBlock ? Blocks.Where(b => b.Page == caret.Block.Page).ToArray() : [caret.Block];
                Point(new(blocks[0], 0, '⠀'), false); Point(new(blocks[^1], blocks[^1].Braille.Length, '⠀', true), true);
                selectedBlock = true; return;
            }
            selectedBlock = false;
            if (ctrl && e.Key is Key.C or Key.X) { e.Handled = true; Copy(e.Key == Key.X); return; }
            if (ctrl && e.Key == Key.V) { e.Handled = true; if (Clipboard.ContainsText()) await InsertAsync(Clipboard.GetText()); return; }
            if (ctrl && e.Key == Key.Enter) { e.Handled = true; await InsertPageBreakAsync(); return; }
            if (ctrl) return;
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            { e.Handled = true; Move(e.Key is Key.Left or Key.Up ? -1 : 1, shift, e.Key is Key.Up or Key.Down); }
            else if (e.Key is Key.Home or Key.End)
            {
                e.Handled = true; var row = CurrentRow();
                if (row.Length > 0)
                {
                    var last = row.LastOrDefault(c => !c.IsInsertion) ?? row[0];
                    Point(e.Key == Key.Home ? row[0] : new(last.Block, last.Offset + (last.IsInsertion ? 0 : 1), '⠀', true), shift);
                }
            }
            else if (e.Key is Key.Delete or Key.Back)
            { e.Handled = true; if (!HasSelection) Move(e.Key == Key.Back ? -1 : 1, true); await InsertAsync(""); }
            else if (e.Key is Key.Enter or Key.Space)
            { e.Handled = true; await InsertAsync(e.Key == Key.Enter ? "\n" : "⠀"); }
            else if (e.Key == Key.Escape) { e.Handled = true; anchor = caret; InvalidateFaces(); }
        };
        PreviewKeyUp += async (_, e) =>
        {
            if (!SixDotEnabled) { input.Reset(); return; }
            var key = e.Key.ToString();
            if (key.Length == 1 && input.IsMapped(key[0]))
            { e.Handled = true; if (input.Release(key[0]) is { } cell) await InsertAsync(cell); }
        };
        LostKeyboardFocus += (_, _) => input.Reset();
        LostMouseCapture += (_, _) => dragSelecting = false;
    }
}
