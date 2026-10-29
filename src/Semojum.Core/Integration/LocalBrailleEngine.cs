using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Semojum.Core.Integration;

/// <summary>Wire adapter for Semojum/braille v3.4.35, commit 74f76d15f3ac6f87c52d75a25882cc580508e51f.</summary>
public sealed class LocalBrailleEngine(BrailleSidecar sidecar, Func<BrailleOptions>? options = null) : ITranslationEngine
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public async Task<string> TranslateAsync(string text, int blockId, CancellationToken cancellationToken) =>
        (await TranslateElementAsync(text, new(), options?.Invoke() ?? new(), cancellationToken)).Cells;

    public async Task<BrailleTranslation> TranslateBlockAsync(EditorBlock block, CancellationToken cancellationToken)
    {
        var sourceRevision = block.SourceRevision; var brailleRevision = block.BrailleRevision;
        var metadataRevision = block.MetadataRevision; var previousBraille = block.Braille;
        var result = await TranslateElementAsync(block.Source, block.Metadata, options?.Invoke() ?? new(), cancellationToken);
        return result.SourceLineMap is { Length: > 0 }
            ? result with { SourceMapStamp = new(sourceRevision, brailleRevision + (previousBraille == result.Cells ? 0 : 1), metadataRevision) }
            : result;
    }

    public async Task<BrailleTranslation> TranslateElementAsync(string text, BlockMetadata metadata, BrailleOptions settings,
        CancellationToken cancellationToken = default)
    {
        // Layout markers belong to composition, not translation (the upstream
        // translator drops them). Translate each surrounding paragraph and retain
        // the marker with explicit logical boundaries in the editable buffer.
        var layoutPattern = @"<!(?:쪽바꿈|장바꿈|상단줄|중단줄|하단줄|상단굵은줄|하단굵은줄|표시작|표중앙|표끝|표구분선|표지시작|표지세로|표지끝)>";
        if (metadata.Type != "table" && !text.Contains("<!표>", StringComparison.Ordinal) && Regex.IsMatch(text, layoutPattern))
        {
            var pieces = Regex.Split(text, "(" + layoutPattern + ")").Where(p => p.Length > 0).ToArray();
            var translated = new List<BrailleTranslation>();
            foreach (var piece in pieces)
                translated.Add(Regex.IsMatch(piece, "^" + layoutPattern + "$") ? BrailleTranslation.Plain(piece) :
                    await TranslateElementAsync(piece, metadata, settings, cancellationToken));
            var compositeCells = string.Join("\n", translated.Select(t => t.Cells)); var offset = 0; var breaks = new List<int>();
            var trail = new List<RuleEvidence>();
            foreach (var t in translated)
            {
                breaks.AddRange(t.Breaks.Select(b => offset + b));
                trail.AddRange((t.RuleTrail ?? []).Select(r => r with { LineNo = 0, ColumnStart = offset + r.ColumnStart, ColumnEnd = offset + r.ColumnEnd }));
                offset += t.Cells.Length + 1;
            }
            return new(compositeCells, breaks.ToArray(), translated.SelectMany(t => t.Dropped).ToArray(), translated.Any(t => t.Blocked), [],
                UnknownTags: translated.SelectMany(t => t.UnknownTags ?? []).Distinct().ToArray(), RuleTrail: trail.ToArray());
        }
        JsonElement response;
        if (metadata.Type == "table" && !text.Contains("<!표>", StringComparison.Ordinal))
        {
            if (metadata.TableSource is null) throw new SidecarException("표 구조 데이터가 없어 표를 점역할 수 없습니다.", "MISSING_TABLE_SOURCE");
            if (metadata.TableDisplayText is { } original && original != text)
                throw new SidecarException("표의 표시 텍스트가 바뀌었습니다. 편집 → 표 구조 편집에서 칸 내용을 수정해 주세요.", "TABLE_STRUCTURE_CHANGED");
            response = await sidecar.InvokeAsync("table", new
            {
                table_source = metadata.TableSource, render_mode = metadata.RenderMode ?? "",
                tn_text = metadata.TranslatorNote ?? "", box_level = metadata.TableBoxLevel,
                korean_grade = settings.KoreanGrade1 ? 1 : 2, english_grade = settings.EnglishGrade1 ? 1 : 2, math_page = metadata.MathPage,
                blank_lines = false, draft_text = true
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            response = await sidecar.InvokeAsync("translate", new
            {
                text = metadata.Type == "formula" && !text.Contains("<!수식>", StringComparison.Ordinal) ? $"<!수식>{text}<!/수식>" : text,
                type = text.Contains("<!표>", StringComparison.Ordinal) ? "table" : metadata.Type switch
                { "formula" => "text", "image" or "figure" or "chart_graph" or "diagram" or "cartoon" => "caption", _ => metadata.Type },
                heading_level = metadata.HeadingLevel,
                korean_grade = settings.KoreanGrade1 ? 1 : 2, english_grade = settings.EnglishGrade1 ? 1 : 2,
                split_choices = settings.ChoiceLayout == "onePerLine", math_page = metadata.MathPage,
                render_mode = metadata.RenderMode ?? "", tn_text = metadata.TranslatorNote ?? "", box_level = metadata.TableBoxLevel,
                // Engine 3.4.38+ puts the element's surrounding blank lines into cells by default; the app lays pages out itself (build_brf), so keep bare element cells.
                blank_lines = false, source_map = true, draft_text = true, line_alignment = true
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        if (!response.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.String)
            throw new SidecarException("점역 결과에 점자 데이터가 없습니다.", "INVALID_RESPONSE");
        var cellText = cells.GetString()!;
        var dropped = ReadArray<DroppedCharacter>(response, "dropped"); var unknown = ReadArray<string>(response, "unknown_tags");
        var isBlocked = response.TryGetProperty("blocked", out var blocked) && blocked.ValueKind == JsonValueKind.True;
        var canMap = metadata.Type == "text" && metadata.HeadingLevel == 0 && !metadata.MathPage
            && settings.ChoiceLayout != "onePerLine" && !isBlocked && dropped.Length == 0 && unknown.Length == 0;
        return new(cellText, ReadArray<int>(response, "breaks"), dropped, isBlocked,
            ReadArray<BrailleDraft>(response, "drafts"), response.TryGetProperty("render_mode", out var mode) ? mode.GetString() : null,
            UnknownTags: unknown,
            RuleTrail: response.TryGetProperty("rule_trail", out var rules) && rules.ValueKind == JsonValueKind.Array ? rules.EnumerateArray().Select(AppliedRules.Read).ToArray() : null,
            Spacing: response.TryGetProperty("spacing", out var spacing) && spacing.ValueKind == JsonValueKind.Object
                ? spacing.Deserialize<BrailleSpacingMetadata>(Json) : null,
            LineAlignment: canMap ? ReadLineAlignment(response, text, cellText) : null,
            SourceLineMap: canMap ? ReadSourceLineMap(response, text, cellText) : null);
    }

    private static BrailleLineAlignment[]? ReadLineAlignment(JsonElement response, string source, string cells)
    {
        try
        {
            return response.TryGetProperty("line_alignment", out var alignment) && alignment.ValueKind == JsonValueKind.Array
                ? ValidateLineAlignment(source, cells, alignment.Deserialize<BrailleLineAlignment[]>(Json)) : null;
        }
        catch (JsonException) { return null; }
    }

    private static BrailleLineAlignment[]? ValidateLineAlignment(string source, string cells, BrailleLineAlignment[]? rows)
    {
        if (rows is not { Length: > 0 } || source.Contains('\r') || !source.Contains("정렬>", StringComparison.Ordinal)) return null;
        var lines = source.Split('\n'); if (lines.Length != rows.Length) return null;
        var sourceAt = 0; var brailleAt = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (brailleAt > cells.Length) return null;
            var tags = Regex.Matches(lines[i], "<!(왼쪽|가운데|오른쪽)정렬>");
            if (tags.Count > 1) return null;
            var clean = Regex.Replace(lines[i], "<!(왼쪽|가운데|오른쪽)정렬>", "");
            if (!Regex.IsMatch(clean, @"\A[가-힣]+(?: +[가-힣]+)*\z")) return null;
            var align = tags.Count == 0 ? "" : tags[0].Groups[1].Value switch { "왼쪽" => "left", "가운데" => "center", _ => "right" };
            var row = rows[i]; var end = cells.IndexOf('\n', brailleAt); if (end < 0) end = cells.Length;
            if (row is null || row.SourceStart != sourceAt || row.SourceEnd != sourceAt + clean.Length || row.Align != align
                || row.BrailleStart < brailleAt || row.BrailleEnd != end || row.BrailleStart >= end
                || cells[brailleAt..row.BrailleStart].Any(c => c is not ('⠀' or ' '))
                || cells[row.BrailleStart..end].Any(c => c is < '\u2800' or > '\u28ff')) return null;
            sourceAt += clean.Length + 1; brailleAt = end + 1;
        }
        return brailleAt == cells.Length + 1 ? rows : null;
    }

    private static SourceLineMap[]? ReadSourceLineMap(JsonElement response, string source, string cells)
    {
        if (!response.TryGetProperty("source_line_map", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > source.Length
            || !Regex.IsMatch(source, @"\A[가-힣]+(?: +[가-힣]+)*(?:\n[가-힣]+(?: +[가-힣]+)*)*\z")) return null;
        try
        {
            var spans = value.Deserialize<SourceLineMap[]>(Json);
            if (spans is not { Length: > 0 }) return null;
            var lastSourceStart = -1; var lastSourceEnd = -1; var lastBrailleEnd = 0;
            foreach (var span in spans)
            {
                if (span is null || span.SourceStart < 0 || span.SourceEnd <= span.SourceStart || span.SourceEnd > source.Length
                    || span.BrailleStart < lastBrailleEnd || span.BrailleEnd <= span.BrailleStart || span.BrailleEnd > cells.Length
                    || span.SourceStart < lastSourceStart || span.SourceStart < lastSourceEnd && span.SourceStart != lastSourceStart
                    || span.SourceStart == lastSourceStart && span.SourceEnd != lastSourceEnd
                    || span.SourceStart > 0 && source[span.SourceStart - 1] != '\n'
                    || span.SourceEnd < source.Length && source[span.SourceEnd] != '\n'
                    || cells.AsSpan(span.BrailleStart, span.BrailleEnd - span.BrailleStart).Contains('\n')
                    || cells[span.BrailleStart..span.BrailleEnd].Any(c => c is < '\u2800' or > '\u28ff')) return null;
                lastSourceStart = span.SourceStart; lastSourceEnd = span.SourceEnd; lastBrailleEnd = span.BrailleEnd;
            }
            return spans;
        }
        catch (JsonException) { return null; }
    }

    public async Task<string> DecodeAsync(string cells, BrailleOptions settings, CancellationToken cancellationToken = default)
    {
        var result = await sidecar.InvokeAsync("decode", new { braille = cells, korean_grade = settings.KoreanGrade1 ? 1 : 2, english = settings.EnglishDocument },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.GetProperty("text").GetString() ?? "";
    }

    public async Task<string> ReviewPriorityAsync(string cells, string source, BlockMetadata metadata, BrailleOptions settings,
        double? ocrConfidence = null, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, object?>
        {
            ["braille"] = cells, ["source"] = source, ["type"] = metadata.Type, ["korean_grade"] = settings.KoreanGrade1 ? 1 : 2
        };
        if (ocrConfidence.HasValue) fields["ocr_confidence"] = ocrConfidence.Value;
        var result = await sidecar.InvokeAsync("grade", fields, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.GetProperty("grade").GetString() switch
        {
            "low" => "먼저 확인", "medium" => "확인", "high" => "나중에 확인",
            _ => throw new SidecarException("알 수 없는 검수 순서입니다.")
        };
    }

    public Task<string> BuildBrfAsync(DocumentSession session, CancellationToken cancellationToken = default) =>
        BuildBrfAsync(SmjFile.Snapshot(session).Manifest, cancellationToken);

    public async Task<string> BuildBrfAsync(SmjManifest snapshot, CancellationToken cancellationToken = default) =>
        (await ComposeAsync(snapshot, cancellationToken)).Brf;

    public async Task<BrailleSymbol[]> SymbolsAsync(CancellationToken cancellationToken = default)
    {
        var result = await sidecar.InvokeAsync("symbols", new { }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReadArray<BrailleSymbol>(result, "symbols");
    }

    public async Task<ComposedBraille> ComposeAsync(SmjManifest snapshot, CancellationToken cancellationToken = default)
    {
        var settings = snapshot.Options;
        var layout = snapshot.Layout;
        layout.Validate();
        FooterRange.Validate(snapshot.FooterRanges ?? []);
        var footer = layout.Footer == "출력 안 함" || string.IsNullOrEmpty(snapshot.FooterText) ? "" :
            (await TranslateElementAsync(snapshot.FooterText, new(Type: "header_footer"), settings, cancellationToken)).Cells;
        string header = "";
        var headers = new Dictionary<int, string>();
        static bool HasAlignment(SmjBlock b) => Regex.IsMatch(b.Source, "<!(왼쪽|가운데|오른쪽)정렬>");
        static BrailleLineAlignment[]? AlignmentFor(SmjBlock b) => !b.HasManualBraille && b.Translation is { } t
            && t.SourceText == b.Source && t.Cells == b.Braille ? ValidateLineAlignment(b.Source, b.Braille, t.LineAlignment) : null;
        static string SingleLineAlignment(SmjBlock b)
        {
            // The legacy composer supports block alignment. It is equivalent to line
            // alignment only when both source and editable braille have exactly one line.
            if (b.Source.IndexOfAny(['\r', '\n']) >= 0 || b.Braille.IndexOfAny(['\r', '\n']) >= 0
                || b.Braille.Length == 0 || b.Braille.Any(c => c != ' ' && c is < '\u2800' or > '\u28ff')) return "";
            var tags = Regex.Matches(b.Source, "<!(왼쪽|가운데|오른쪽)정렬>");
            if (tags.Count != 1 || Regex.Replace(b.Source, "<!(왼쪽|가운데|오른쪽)정렬>", "").Trim().Length == 0) return "";
            return tags[0].Groups[1].Value switch { "왼쪽" => "left", "가운데" => "center", "오른쪽" => "right", _ => "" };
        }
        object Sources(IEnumerable<SmjBlock> blocks) => blocks.GroupBy(b => b.Page).Select(page => new
        {
            orig_page = page.Key,
            blocks = page.Select((b, order) => new
            {
                id = b.Id, order, type = b.Metadata.Type, heading_level = b.Metadata.HeadingLevel,
                braille = !b.HasManualBraille && IsLayoutMarker(b.Source) ? b.Source.Trim() : b.Braille,
                breaks = b.HasManualBraille ? Array.Empty<int>() : b.Translation?.Breaks ?? [],
                box_borders = !b.HasManualBraille && b.Translation?.Cells == b.Braille ? b.Translation?.Spacing?.BoxBorders : null,
                corrected_text = !b.HasManualBraille && b.Translation?.Cells == b.Braille ? b.Translation?.Spacing?.CorrectedText : null,
                question_lead = !b.HasManualBraille && b.Translation?.Cells == b.Braille ? b.Translation?.Spacing?.QuestionLead : null,
                align = SingleLineAlignment(b),
                line_alignments = AlignmentFor(b)?.Select(a => new { braille_start = a.BrailleStart, braille_end = a.BrailleEnd, align = a.Align }).ToArray(),
                alignment_unknown = HasAlignment(b) && AlignmentFor(b) is null && SingleLineAlignment(b).Length == 0
            }).ToArray()
        }).ToArray();
        var footers = new Dictionary<int, string>();
        var footerAligns = new Dictionary<int, string>();
        var footerRules = new List<object>();
        foreach (var range in snapshot.FooterRanges ?? [])
            footerRules.Add(new { start = range.Start, end = range.End,
                cells = range.Text.Length == 0 ? "" : (await TranslateElementAsync(range.Text, new(Type: "header_footer"), settings, cancellationToken)).Cells,
                align = range.Alignment });
        Task<JsonElement> Build(object sources, bool pad) => sidecar.InvokeAsync("compose_editor", new { job = new
            {
                sources, footer, header, headers, footers, footer_aligns = footerAligns, footer_ranges = footerRules,
                opts = new
                {
                    cols = layout.Columns, rows = layout.Lines, pad_to_even = pad,
                    page_row_pages = PagePolicy(layout.PageNumbers), footer_pages = PagePolicy(layout.Footer),
                    number_ranges = layout.NumberRanges?.Select(r => new[] { r.Start, r.End ?? int.MaxValue }).ToArray(),
                    show_change_line = layout.ShowChangeLine,
                    first_braille_page = layout.StartPage,
                    footer_align = layout.FooterAlign, wrap = settings.LineBreakUnit,
                    cover_orig_page_count = layout.CoverPages, orig_page_first_no = layout.SourcePageStart,
                    show_orig_page = layout.ShowSourcePageNumber,
                    show_braille_page = layout.ShowBraillePageNumber
                }
            } }, cancellationToken: cancellationToken);

        var changes = new SortedDictionary<int, string>();
        var coverFaces = 0;
        var hasHeaders = snapshot.Blocks.Any(b => b.Source.Contains("<!머리말>", StringComparison.Ordinal));
        if (hasHeaders && layout.CoverPages > 0)
        {
            var coverNumbers = snapshot.Blocks.Select(b => b.Page).Distinct().Take(layout.CoverPages).ToHashSet();
            var cover = await Build(Sources(snapshot.Blocks.Where(b => coverNumbers.Contains(b.Page))), false).ConfigureAwait(false);
            coverFaces = DocumentExport.BrfPages(cover.GetProperty("brf").GetString() ?? "").Length;
        }
        for (var index = 0; index < snapshot.Blocks.Length; index++)
        {
            var block = snapshot.Blocks[index];
            foreach (Match marker in Regex.Matches(block.Source, "<!머리말>(.*?)<!/머리말>", RegexOptions.Singleline))
            {
                // Locate the source anchor using an exact translated prefix. A changed manual prefix has no safe mapping.
                var sourcePrefix = block.Source[..marker.Index];
                var prefix = IsNonPrintingTag(sourcePrefix) || string.IsNullOrWhiteSpace(sourcePrefix) ? "" :
                    (await TranslateElementAsync(sourcePrefix, block.Metadata, settings, cancellationToken)).Cells;
                if (!block.Braille.StartsWith(prefix, StringComparison.Ordinal))
                    throw new ArgumentException($"{block.Page}쪽 머리말 위치를 직접 수정한 점자와 연결할 수 없습니다. 해당 블록을 재점역하거나 블록 시작에 머리말을 넣어 주세요.");
                // A visible probe keeps the anchor's blank row from being trimmed. It is never returned or saved.
                var probe = prefix.Length < block.Braille.Length || prefix.Length == 0 || prefix.EndsWith('\n') ? prefix + "⠿" : prefix;
                var partial = block with { Braille = probe, Translation = block.Translation is null ? null : block.Translation with { Breaks = block.Translation.Breaks.Where(b => b <= prefix.Length).ToArray() } };
                var composedPrefix = await Build(Sources(snapshot.Blocks.Take(index).Append(partial)), false).ConfigureAwait(false);
                var faces = DocumentExport.BrfPages(composedPrefix.GetProperty("brf").GetString() ?? "").Length;
                var face = checked(layout.StartPage + Math.Max(0, faces - coverFaces - 1));
                var text = marker.Groups[1].Value;
                var translated = text.Length == 0 ? "" : (await TranslateElementAsync(text, new(Type: "header_footer"), settings, cancellationToken)).Cells;
                changes[face] = translated;
                // The latest value becomes the default for following faces; earlier faces retain their prior values.
                header = changes.Last().Value; headers.Clear();
                var ordered = changes.ToArray(); var changeIndex = 0; var current = "";
                for (var page = layout.StartPage; page < changes.Last().Key; page++)
                {
                    while (changeIndex < ordered.Length && ordered[changeIndex].Key <= page) current = ordered[changeIndex++].Value;
                    headers[page] = current;
                }
            }
        }
        var sources = Sources(snapshot.Blocks);
        var result = await Build(sources, layout.PadToEven).ConfigureAwait(false);
        var brf = result.GetProperty("brf").GetString() ?? throw new SidecarException("BRF 결과가 없습니다.");
        if (brf.Any(c => c > 127)) throw new SidecarException("BRF 응답에 ASCII가 아닌 문자가 있습니다.");
        var replaced = result.TryGetProperty("replaced", out var r) ? r.Deserialize<Dictionary<string, int>>() ?? [] : [];
        var pages = result.GetProperty("pages").Deserialize<string[][]>() ?? [];
        var maps = result.GetProperty("maps").Deserialize<int[]?[][][]>() ?? [];
        var ends = result.GetProperty("ends").Deserialize<int[]?[][]>() ?? [];
        var kinds = result.GetProperty("kinds").Deserialize<string[][]>() ?? [];
        var numbers = result.GetProperty("numbers").Deserialize<int?[]>() ?? [];
        CellAddress? Address(int[]? value) => value is { Length: 2 } ? new(value[0], value[1]) : null;
        var spacingUnknown = result.TryGetProperty("spacing_unknown", out var unknown)
            ? unknown.Deserialize<int[]>() ?? snapshot.Blocks.Select(b => b.Id).ToArray()
            : snapshot.Blocks.Select(b => b.Id).ToArray();
        var alignmentProtocol = result.TryGetProperty("alignment_protocol", out var protocol) && protocol.TryGetInt32(out var version) && version == 1;
        var alignmentUnknown = !alignmentProtocol ? snapshot.Blocks.Where(b => HasAlignment(b) && SingleLineAlignment(b).Length == 0).Select(b => b.Id).ToArray() : result.TryGetProperty("alignment_unknown", out var unaligned)
            ? unaligned.Deserialize<int[]>() ?? snapshot.Blocks.Where(b => HasAlignment(b) && AlignmentFor(b) is null && SingleLineAlignment(b).Length == 0).Select(b => b.Id).ToArray()
            : snapshot.Blocks.Where(b => HasAlignment(b) && AlignmentFor(b) is null && SingleLineAlignment(b).Length == 0).Select(b => b.Id).ToArray();
        return new(brf, replaced, pages.Select((lines, i) => new ComposedPage(numbers[i], lines,
            maps[i].Select(row => row.Select(Address).ToArray()).ToArray(), ends[i].Select(Address).ToArray(), kinds[i])).ToArray(), spacingUnknown, alignmentUnknown);
    }

    private static string PagePolicy(string value) => value switch { "모든 페이지" => "every", "짝수 페이지" => "even", "출력 안 함" => "none", _ => "odd" };

    public static bool IsLayoutMarker(string source) => source.Trim() is "<!쪽바꿈>" or "<!장바꿈>" or "<!상단줄>" or "<!중단줄>" or "<!하단줄>"
        or "<!상단굵은줄>" or "<!하단굵은줄>" or "<!표시작>" or "<!표중앙>" or "<!표끝>" or "<!표구분선>" or "<!표지시작>" or "<!표지세로>" or "<!표지끝>";

    public static bool IsNonPrintingTag(string source) => source.Length > 0 &&
        Regex.Replace(source, "<!머리말>.*?<!/머리말>|<!(?:왼쪽|가운데|오른쪽)정렬>", "", RegexOptions.Singleline).Trim().Length == 0;

    public async Task<string[][]> ReadBrfAsync(string brf, CancellationToken cancellationToken = default)
    {
        var result = await sidecar.InvokeAsync("read_brf", new { brf }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.GetProperty("pages").Deserialize<string[][]>() ?? [];
    }

    private static T[] ReadArray<T>(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        ? value.Deserialize<T[]>(Json) ?? [] : [];
}
