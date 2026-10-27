namespace Semojum.Core;

/// <summary>Spots the AI marked with ■ because the original could not be read (plan 9-2 #9). Blocked blocks carry no text to count.</summary>
public static class UnreadableText
{
    public const char Marker = '\u25A0';
    public static int Count(DocumentSession session) => session.Blocks.Where(b => !b.Metadata.IsBlocked).Sum(b => b.Source.Count(c => c == Marker));
    public static (EditorBlock Block, int Offset)? First(DocumentSession session)
    {
        foreach (var block in session.Blocks)
        {
            if (block.Metadata.IsBlocked) continue;
            var offset = block.Source.IndexOf(Marker);
            if (offset >= 0) return (block, offset);
        }
        return null;
    }
}
