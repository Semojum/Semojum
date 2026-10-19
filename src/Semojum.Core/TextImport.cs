namespace Semojum.Core;

public static class TextImport
{
    // The real engine accepts at most 1,000 source characters per request.
    // Prefer a paragraph boundary and never split a Unicode surrogate pair or a tagged document.
    public static string[] Split(string text, int maximum = 1000)
    {
        if (maximum < 2) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (text.Length <= maximum || text.Contains("<!", StringComparison.Ordinal)) return [text];
        var chunks = new List<string>(); var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(maximum, text.Length - start);
            if (start + length < text.Length)
            {
                var newline = text.LastIndexOf('\n', start + length - 1, length);
                if (newline >= start + length / 2) length = newline - start + 1;
                if (char.IsHighSurrogate(text[start + length - 1]) && char.IsLowSurrogate(text[start + length])) length--;
                if (text[start + length - 1] == '\r' && text[start + length] == '\n') length--;
            }
            chunks.Add(text.Substring(start, length)); start += length;
        }
        return chunks.ToArray();
    }
}
