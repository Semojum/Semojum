using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Semojum.Core.Integration;

/// <summary>Wire adapter for Semojum/braille v3.4.24, commit 976b2048d1a1ad0b31a869001f80f9fa260b93af.</summary>
public sealed class LocalBrailleEngine(BrailleSidecar sidecar, Func<BrailleOptions>? options = null) : ITranslationEngine
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public async Task<string> TranslateAsync(string text, int blockId, CancellationToken cancellationToken) =>
        (await TranslateElementAsync(text, new(), options?.Invoke() ?? new(), cancellationToken)).Cells;

    public Task<BrailleTranslation> TranslateBlockAsync(EditorBlock block, CancellationToken cancellationToken) =>
        TranslateElementAsync(block.Source, block.Metadata, options?.Invoke() ?? new(), cancellationToken);

    public async Task<BrailleTranslation> TranslateElementAsync(string text, BlockMetadata metadata, BrailleOptions settings,
        CancellationToken cancellationToken = default)
    {
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
                korean_grade = settings.KoreanGrade1 ? 1 : 2, english_grade = settings.EnglishGrade1 ? 1 : 2, math_page = metadata.MathPage
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
                render_mode = metadata.RenderMode ?? "", tn_text = metadata.TranslatorNote ?? "", box_level = metadata.TableBoxLevel
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        if (!response.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.String)
            throw new SidecarException("점역 결과에 점자 데이터가 없습니다.", "INVALID_RESPONSE");
        return new(cells.GetString()!, ReadArray<int>(response, "breaks"), ReadArray<DroppedCharacter>(response, "dropped"),
            response.TryGetProperty("blocked", out var blocked) && blocked.ValueKind == JsonValueKind.True,
            ReadArray<BrailleDraft>(response, "drafts"), response.TryGetProperty("render_mode", out var mode) ? mode.GetString() : null,
            UnknownTags: ReadArray<string>(response, "unknown_tags"));
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
        var footer = string.IsNullOrEmpty(snapshot.FooterText) ? "" :
            (await TranslateElementAsync(snapshot.FooterText, new(Type: "header_footer"), settings, cancellationToken)).Cells;
        var headerText = snapshot.Blocks.SelectMany(b => Regex.Matches(b.Source, "<!머리말>(.*?)<!/머리말>", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)).LastOrDefault();
        var header = headerText is null ? "" : (await TranslateElementAsync(headerText, new(Type: "header_footer"), settings, cancellationToken)).Cells;
        var sources = snapshot.Blocks.GroupBy(b => b.Page).Select(page => new
        {
            orig_page = page.Key,
            blocks = page.Select((b, order) => new
            {
                order, heading_level = b.Metadata.HeadingLevel,
                braille = !b.HasManualBraille && IsLayoutMarker(b.Source) ? b.Source.Trim() : b.Braille,
                breaks = b.HasManualBraille ? Array.Empty<int>() : b.Translation?.Breaks ?? [],
                align = Regex.Matches(b.Source, "<!(왼쪽|가운데|오른쪽)정렬>").LastOrDefault()?.Groups[1].Value switch
                { "왼쪽" => "left", "가운데" => "center", "오른쪽" => "right", _ => "" }
            }).ToArray()
        }).ToArray();
        var result = await sidecar.InvokeAsync("build_brf", new
        {
            job = new
            {
                sources, footer, header,
                opts = new
                {
                    cols = layout.Columns, rows = layout.Lines, pad_to_even = layout.PadToEven,
                    page_row_pages = layout.PageNumbers == "모든 페이지" ? "every" : "odd",
                    first_braille_page = layout.StartPage,
                    footer_align = layout.FooterAlign, wrap = settings.LineBreakUnit,
                    cover_orig_page_count = layout.CoverPages, orig_page_first_no = layout.SourcePageStart,
                    show_orig_page = layout.PageNumbers != "출력 안 함" && layout.ShowSourcePageNumber,
                    show_braille_page = layout.PageNumbers != "출력 안 함" && layout.ShowBraillePageNumber
                }
            }
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var brf = result.GetProperty("brf").GetString() ?? throw new SidecarException("BRF 결과가 없습니다.");
        if (brf.Any(c => c > 127)) throw new SidecarException("BRF 응답에 ASCII가 아닌 문자가 있습니다.");
        var replaced = result.TryGetProperty("replaced", out var r) ? r.Deserialize<Dictionary<string, int>>() ?? [] : [];
        return new(brf, replaced);
    }

    public static bool IsLayoutMarker(string source) => source.Trim() is "<!쪽바꿈>" or "<!장바꿈>" or "<!상단줄>" or "<!중단줄>" or "<!하단줄>"
        or "<!상단굵은줄>" or "<!하단굵은줄>" or "<!표시작>" or "<!표중앙>" or "<!표끝>" or "<!표구분선>";

    public async Task<string[][]> ReadBrfAsync(string brf, CancellationToken cancellationToken = default)
    {
        var result = await sidecar.InvokeAsync("read_brf", new { brf }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.GetProperty("pages").Deserialize<string[][]>() ?? [];
    }

    private static T[] ReadArray<T>(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        ? value.Deserialize<T[]>(Json) ?? [] : [];
}
