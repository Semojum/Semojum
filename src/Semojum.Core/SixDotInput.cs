namespace Semojum.Core;

/// <summary>Emit one cell after every key in a chord has been released.</summary>
public sealed class SixDotInput
{
    private readonly HashSet<char> held = [];
    private readonly Dictionary<char, int> mapping = new() { ['F'] = 1, ['D'] = 2, ['S'] = 4, ['J'] = 8, ['K'] = 16, ['L'] = 32 };
    private int chord;

    public void Configure(string keys)
    {
        var normalized = keys.ToUpperInvariant().Where(char.IsAsciiLetter).ToArray();
        if (normalized.Length != 6 || normalized.Distinct().Count() != 6)
            throw new ArgumentException("서로 다른 영문 키 6개를 지정해 주세요.");
        Reset();
        mapping.Clear();
        for (var i = 0; i < 6; i++) mapping[normalized[i]] = 1 << i;
    }

    public bool IsMapped(char key) => mapping.ContainsKey(char.ToUpperInvariant(key));
    public bool Press(char key)
    {
        key = char.ToUpperInvariant(key);
        if (!mapping.TryGetValue(key, out var bit)) return false;
        if (held.Add(key)) chord |= bit;
        return true;
    }

    public string? Release(char key)
    {
        if (!held.Remove(char.ToUpperInvariant(key)) || held.Count > 0) return null;
        var result = char.ConvertFromUtf32(0x2800 + chord);
        chord = 0;
        return result;
    }
    public void Reset() { held.Clear(); chord = 0; }
}
