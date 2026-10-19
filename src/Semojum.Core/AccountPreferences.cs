using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Semojum.Core;

public sealed record EditorAppearance(string TextColor = "#1F1F1F", string BackgroundColor = "#FFFFFF", string CurrentLineColor = "#FFF5A3",
    string SelectionColor = "#CCE4F7", string FontFamily = "Malgun Gothic", double TextSize = 14, double BrailleSize = 22,
    double LineSpacing = 1.3, double CursorWidth = 2, bool HighContrast = false, bool SpeakDots = false, int SpeechRate = 0,
    string SelectionTextColor = "#1F1F1F", string BrailleColor = "#1F1F1F", string MarkerColor = "#808080",
    string LineNumberColor = "#666666", string LineNumberBackground = "#F3F3F3", string TagColor = "#2D609E",
    string OutlineColor = "#F25D5D", double OutlineWidth = 1, bool Bold = false, bool ShowLineNumbers = true,
    bool ShowFormattingMarks = false, int RecentCount = 10, bool ShowToolbar = true);
public sealed record OutputPreferences(PageLayout? Layout = null, string LineBreakUnit = "syllable", bool Duplex = true);
public sealed record AccountPreferences(string SixDotKeys = "F D S J K L", bool SpaceEnabled = true, bool EnterEnabled = true,
    string[]? TextSnippets = null, string[]? BrailleSnippets = null, EditorAppearance? Appearance = null, OutputPreferences? Output = null);

/// <summary>Device-local account preferences. This stores no document snapshots or authentication tokens.</summary>
public sealed class AccountPreferenceStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string PathFor(string loginId) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loginId))) + ".json");
    public AccountPreferences Load(string loginId)
    {
        var path = PathFor(loginId);
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("계정 설정 파일이 너무 큽니다.");
        var value = JsonSerializer.Deserialize<AccountPreferences>(File.ReadAllText(path), Json) ?? new();
        Validate(value); return value;
    }
    public async Task SaveAsync(string loginId, AccountPreferences value)
    {
        Validate(value); Directory.CreateDirectory(directory);
        var path = PathFor(loginId); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Validate(AccountPreferences value)
    {
        new SixDotInput().Configure(value.SixDotKeys);
        value.Output?.Layout?.Validate();
        if (value.Output is { LineBreakUnit: not ("syllable" or "word") }) throw new ArgumentException("출력 줄바꿈 옵션이 올바르지 않습니다.");
        foreach (var slots in new[] { value.TextSnippets, value.BrailleSnippets })
            if (slots is not null && (slots.Length != 10 || slots.Any(s => s is null || s.Length > 10000))) throw new ArgumentException("상용구는 패널별 10개, 각 10,000자까지입니다.");
        if (value.BrailleSnippets?.Any(s => !DocumentSearch.IsBrailleText(s)) == true) throw new ArgumentException("점자 상용구에는 점자와 공백만 넣을 수 있습니다.");
        if (value.Appearance is { } a)
        {
            if (new[] { a.TextSize, a.BrailleSize, a.CursorWidth, a.LineSpacing, a.OutlineWidth }.Any(v => !double.IsFinite(v)) || a.TextSize is < 8 or > 72 || a.BrailleSize is < 8 or > 72 || a.CursorWidth is < 1 or > 8 || a.LineSpacing is < 1 or > 3 || a.SpeechRate is < -10 or > 10 || a.OutlineWidth is < 0 or > 8 || a.RecentCount is < 0 or > 50 || string.IsNullOrWhiteSpace(a.FontFamily))
                throw new ArgumentException("글자 크기 8–72, 커서 폭 1–8, 줄 간격 1–3, 윤곽선 0–8, 최근 파일 0–50, 읽기 속도 -10–10으로 설정해 주세요.");
            foreach (var color in new[] { a.TextColor, a.BackgroundColor, a.CurrentLineColor, a.SelectionColor, a.SelectionTextColor, a.BrailleColor, a.MarkerColor, a.LineNumberColor, a.LineNumberBackground, a.TagColor, a.OutlineColor })
                if (color is null || !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("색상은 #RRGGBB 형식으로 입력해 주세요.");
            if (new[] { a.TextColor, a.BrailleColor, a.TagColor, a.MarkerColor, a.SelectionTextColor }.Any(c => c.Equals("#B22222", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("#B22222는 점자 직접 수정 표시용 색상입니다. 다른 글자 색을 선택해 주세요.");
        }
    }
}
