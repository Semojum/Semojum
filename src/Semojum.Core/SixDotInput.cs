namespace Semojum.Core;

/// <summary>Emit one cell after every key in a chord has been released.</summary>
public sealed class SixDotInput
{
    private readonly HashSet<char> held = [];
    private readonly Dictionary<char, int> mapping = new() { ['F'] = 1, ['D'] = 2, ['S'] = 4, ['J'] = 8, ['K'] = 16, ['L'] = 32 };
    private int chord;

    public void Configure(string keys)
    {
        if (keys is null) throw new ArgumentException("서로 다른 영문, 숫자 또는 기호 키 6개를 지정해 주세요.");
        var raw = keys.Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (raw.Length != 6 || raw.Any(c => c < '!' || c > '~'))
            throw new ArgumentException("서로 다른 영문, 숫자 또는 기호 키 6개를 지정해 주세요.");
        var normalized = raw.Select(PhysicalKey).ToArray();
        if (normalized.Distinct().Count() != 6)
            throw new ArgumentException("서로 다른 실제 키 6개를 지정해 주세요.");
        Reset();
        mapping.Clear();
        for (var i = 0; i < 6; i++) mapping[normalized[i]] = 1 << i;
    }

    // Shifted punctuation is the same physical key as its unshifted counterpart.
    public static char PhysicalKey(char key) => char.ToUpperInvariant(key) switch
    {
        '!' => '1', '@' => '2', '#' => '3', '$' => '4', '%' => '5',
        '^' => '6', '&' => '7', '*' => '8', '(' => '9', ')' => '0',
        '_' => '-', '+' => '=', '{' => '[', '}' => ']', '|' => '\\',
        ':' => ';', '"' => '\'', '<' => ',', '>' => '.', '?' => '/', '~' => '`',
        var c => c
    };

    public bool IsMapped(char key) => mapping.ContainsKey(PhysicalKey(key));
    public bool Press(char key)
    {
        key = PhysicalKey(key);
        if (!mapping.TryGetValue(key, out var bit)) return false;
        if (held.Add(key)) chord |= bit;
        return true;
    }

    public string? Release(char key)
    {
        if (!held.Remove(PhysicalKey(key)) || held.Count > 0) return null;
        var result = char.ConvertFromUtf32(0x2800 + chord);
        chord = 0;
        return result;
    }
    public void Reset() { held.Clear(); chord = 0; }
}
