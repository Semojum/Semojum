namespace Semojum.Core;

public sealed record GridCell(EditorBlock Block, int Offset, char Value, bool IsInsertion = false, string Prefix = "");
public sealed record BrailleGridFace(int Number, GridCell?[][] Rows, string[]? Text = null, string[]? Kinds = null);

/// <summary>Reversible editing coordinates. Soft wraps and blank padding never enter stored block text.</summary>
public static class BrailleGrid
{
    public static IReadOnlyList<BrailleGridFace> FromComposition(ComposedBraille composition, IReadOnlyList<EditorBlock> blocks, PageLayout layout)
    {
        var byId = blocks.ToDictionary(b => b.Id);
        return (composition.Pages ?? []).Select((page, index) =>
        {
            var rows = new GridCell?[layout.Lines][];
            for (var r = 0; r < rows.Length; r++)
            {
                rows[r] = new GridCell?[layout.Columns];
                GridCell? Cell(CellAddress? address, char value, bool insertion)
                {
                    if (address is null || !byId.TryGetValue(address.BlockId, out var b)) return null;
                    if (address.Offset < 0 || address.Offset > b.Braille.Length || !insertion && address.Offset == b.Braille.Length)
                        throw new InvalidDataException("조판 좌표가 편집 내용과 일치하지 않습니다.");
                    if (!insertion && b.Braille[address.Offset] != value && !(b.Braille[address.Offset] is ' ' or '\t' && value == '⠀'))
                        throw new InvalidDataException("조판 이후 점자 내용이 바뀌었습니다.");
                    return new(b, address.Offset, value, insertion);
                }
                for (var c = 0; c < Math.Min(layout.Columns, page.Cells[r].Length); c++)
                    rows[r][c] = Cell(page.Cells[r][c], page.Lines[r][c], false);
                var endColumn = page.Cells[r].Length;
                if (endColumn < layout.Columns && page.Ends[r] is { } end)
                    for (var c = endColumn; c < layout.Columns; c++)
                        if (Cell(end, '⠀', true) is { } insertion) rows[r][c] = insertion with { Prefix = new string('⠀', c - endColumn) };
                if (page.Kinds[r] == "padding")
                {
                    var previous = r - 1;
                    while (previous >= 0 && page.Kinds[previous] == "padding") previous--;
                    if (previous >= 0 && page.Ends[previous] is { } anchor)
                        for (var c = 0; c < layout.Columns; c++)
                            if (Cell(anchor, '⠀', true) is { } insertion) rows[r][c] = insertion with { Prefix = new string('\n', r - previous) + new string('⠀', c) };
                }
            }
            return new BrailleGridFace(page.Number ?? index + 1, rows, page.Lines, page.Kinds);
        }).ToArray();
    }

    public static IReadOnlyList<BrailleGridFace> Project(IEnumerable<EditorBlock> blocks, PageLayout layout)
    {
        layout.Validate();
        var faces = new List<BrailleGridFace>();
        var rows = new List<GridCell?[]>();
        GridCell?[] row = new GridCell?[layout.Columns]; var column = 0;
        void FlushRow()
        {
            rows.Add(row); row = new GridCell?[layout.Columns]; column = 0;
            if (rows.Count == layout.Lines) { faces.Add(new(layout.StartPage + faces.Count, rows.ToArray())); rows.Clear(); }
        }
        foreach (var block in blocks.Where(b => !b.Metadata.IsBlocked))
        {
            // A block boundary starts a new editing line. Padding is view-only.
            if (column > 0) FlushRow();
            if (block.Braille.Length == 0) { row[0] = new(block, 0, '⠀'); FlushRow(); continue; }
            for (var offset = 0; offset < block.Braille.Length; offset++)
            {
                var c = block.Braille[offset];
                if (c == '\r' && offset + 1 < block.Braille.Length && block.Braille[offset + 1] == '\n') continue;
                if (c is '\r' or '\n')
                {
                    // A newline after an exact-width row does not create an additional soft-wrap line.
                    if (column == layout.Columns) FlushRow();
                    else { row[column] = new(block, offset, '⠀'); FlushRow(); }
                    continue;
                }
                if (column == layout.Columns) FlushRow();
                row[column++] = new(block, offset, c is ' ' or '\t' ? '⠀' : c);
            }
            if (column < layout.Columns) row[column] = new(block, block.Braille.Length, '⠀');
            FlushRow();
        }
        if (rows.Count > 0 || faces.Count == 0)
        {
            while (rows.Count < layout.Lines) rows.Add(new GridCell?[layout.Columns]);
            faces.Add(new(layout.StartPage + faces.Count, rows.ToArray()));
        }
        return faces;
    }
}
