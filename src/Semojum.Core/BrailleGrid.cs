namespace Semojum.Core;

public sealed record GridCell(EditorBlock Block, int Offset, char Value);
public sealed record BrailleGridFace(int Number, GridCell?[][] Rows);

/// <summary>Reversible editing coordinates. Soft wraps and blank padding never enter stored block text.</summary>
public static class BrailleGrid
{
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
