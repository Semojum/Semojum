using System.Text.Json.Serialization;

namespace Semojum.Core;

public sealed record BrailleOptions(bool KoreanGrade1 = false, bool EnglishGrade1 = false,
    bool EnglishDocument = false, string LineBreakUnit = "syllable", string MixedRule = "korean",
    string ConsecutiveSpace = "single", string ChoiceLayout = "merge", Dictionary<string, VisualDescription>? VisualDescriptions = null);
public sealed record VisualDescription(string[] Options, string DefaultOption);
public sealed record DroppedCharacter(string Text, int Count, string Flag);
public sealed record BrailleDraft(string RenderMode, string Label, string Cells, int[] Breaks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceText = null);
public sealed record BrailleSpacingMetadata(bool BoxBorders, string CorrectedText, bool QuestionLead);
public sealed record SourceLineMap(int SourceStart, int SourceEnd, int BrailleStart, int BrailleEnd);
public sealed record BrailleLineAlignment(int SourceStart, int SourceEnd, int BrailleStart, int BrailleEnd, string Align);
public sealed record SourceMapStamp(int SourceRevision, int BrailleRevision, int MetadataRevision);
public sealed record BrailleTranslation(string Cells, int[] Breaks, DroppedCharacter[] Dropped,
    bool Blocked, BrailleDraft[] Drafts, string? RenderMode = null, string? SourceText = null, string[]? UnknownTags = null, RuleEvidence[]? RuleTrail = null,
    BrailleSpacingMetadata? Spacing = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrailleLineAlignment[]? LineAlignment = null,
    [property: JsonIgnore] SourceLineMap[]? SourceLineMap = null,
    [property: JsonIgnore] SourceMapStamp? SourceMapStamp = null)
{
    public static BrailleTranslation Plain(string cells) => new(cells, [], [], false, []);
    public bool NeedsReview => Blocked || Dropped.Length != 0 || UnknownTags?.Length > 0;
}
public sealed record BrailleSymbol(string Char, string Cells, string Group);
public sealed record CellAddress(int BlockId, int Offset);
public sealed record ComposedPage(int? Number, string[] Lines, CellAddress?[][] Cells, CellAddress?[] Ends, string[] Kinds);
public sealed record ComposedBraille(string Brf, Dictionary<string, int> Replaced, ComposedPage[]? Pages = null, int[]? SpacingUnknownBlockIds = null, int[]? AlignmentUnknownBlockIds = null);
/// <param name="ReviewFlag">The AI's line-number signal for the element (BE 2026-11-01): "open" unresolved warning, "sug" suggested fix, "note" memo; null when none.</param>
public sealed record BlockMetadata(string Type = "text", int HeadingLevel = 0, bool MathPage = false,
    string? TableSource = null, string? RenderMode = null, string? TranslatorNote = null, int TableBoxLevel = 0,
    double[]? BoundingBox = null, int? SelectedIndex = null, string? TableDisplayText = null,
    AlternativeDraft[]? Alternatives = null, RuleEvidence[]? Rules = null, string? OriginalSource = null,
    string? VisualSubtype = null, bool IsBlocked = false, double? OcrConfidence = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReviewFlag = null);
/// <param name="Option">The draft's fixed option number (BE 2026-11-01: 1 생략 · 2 기본 설명 · 6 별책 참조 · 7 줄글 …). Null on older pages and files.</param>
public sealed record AlternativeDraft(string Label, string Text, string? TaggedText, string? LegacyBraille, string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Option = null);
public enum RuleEvidenceOrigin { Unknown, TextPanel, BraillePanel }
/// <param name="Axis">What the columns count (engine 3.4.44): "braille" cells (engine) or "print" source characters (AI server). Null in older data; read by origin.</param>
public sealed record RuleEvidence(string RuleId, string Source, string Section, string Title, string Excerpt,
    int LineNo, int ColumnStart, int ColumnEnd, string? Tag = null, RuleEvidenceOrigin Origin = RuleEvidenceOrigin.Unknown,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Axis = null);

/// <summary>Uses engine break offsets; a missing map falls back to whitespace, never arbitrary syllable guesses.</summary>
public static class BrailleWrapping
{
    public static IReadOnlyList<string> Fold(string cells, IReadOnlyList<int> breaks, int columns,
        bool center = false, bool wordOnly = false)
    {
        if (columns < 8) throw new ArgumentOutOfRangeException(nameof(columns));
        var result = new List<string>();
        var offset = 0;
        // Python offsets only equal .NET indices for BMP. The engine documents this fallback.
        var useBreaks = !wordOnly && !cells.Any(char.IsSurrogate);
        foreach (var line in cells.Split('\n'))
        {
            var positions = useBreaks ? breaks.Where(b => b > offset && b < offset + line.Length).Select(b => b - offset).ToArray() : [];
            offset += line.Length + 1;
            if (positions.Length == 0)
                positions = Enumerable.Range(1, Math.Max(0, line.Length - 1))
                    .Where(i => IsBlank(line[i]) && !IsBlank(line[i - 1])).ToArray();
            var fragments = new List<string>();
            var start = 0;
            while (line.Length - start > columns)
            {
                var split = positions.Where(p => p > start && p <= start + columns).DefaultIfEmpty(-1).Max();
                if (split < 0)
                {
                    split = start + columns;
                    if (line[split - 1] == '⠠' && line[split] == '⠄') split--;
                    if (char.IsHighSurrogate(line[split - 1]) && char.IsLowSurrogate(line[split])) split--;
                }
                fragments.Add(line[start..split]); start = split;
                while (start < line.Length && IsBlank(line[start])) start++;
            }
            if (start < line.Length || fragments.Count == 0) fragments.Add(line[start..]);
            result.AddRange(center && line.Length > columns
                ? fragments.Select(s => new string('⠀', (columns - s.Length) / 2) + s) : fragments);
        }
        return result;
    }
    private static bool IsBlank(char c) => c is ' ' or '⠀';
}
