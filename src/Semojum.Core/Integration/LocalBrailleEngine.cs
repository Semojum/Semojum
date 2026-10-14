using System.Text.Json;
using System.Text.Json.Serialization;

namespace Semojum.Core.Integration;

/// <summary>Wire adapter for Semojum/braille v3.4.17, commit 74d6758bf34cdb028c6121c7ebe7fb568ad35fec.</summary>
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
        if (metadata.Type == "table")
        {
            if (metadata.TableSource is null) throw new SidecarException("표 구조 데이터가 없어 표를 점역할 수 없습니다.", "MISSING_TABLE_SOURCE");
            if (metadata.TableDisplayText is { } original && original != text)
                throw new SidecarException("표의 표시 텍스트가 바뀌었습니다. 표 구조 편집 규칙이 확정되기 전에는 기존 표를 그대로 재점역할 수 없습니다.", "TABLE_STRUCTURE_CHANGED");
            response = await sidecar.InvokeAsync("table", new
            {
                table_source = metadata.TableSource, render_mode = metadata.RenderMode ?? "",
                tn_text = metadata.TranslatorNote ?? "", box_level = metadata.TableBoxLevel,
                korean_grade1 = settings.KoreanGrade1, english_grade1 = settings.EnglishGrade1, math_page = metadata.MathPage
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            response = await sidecar.InvokeAsync("translate", new
            {
                text, type = metadata.Type, heading_level = metadata.HeadingLevel,
                korean_grade1 = settings.KoreanGrade1, english_grade1 = settings.EnglishGrade1,
                math_page = metadata.MathPage, force_roman = metadata.Type == "header_footer"
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        if (!response.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.String)
            throw new SidecarException("점역 결과에 점자 데이터가 없습니다.", "INVALID_RESPONSE");
        return new(cells.GetString()!, ReadArray<int>(response, "breaks"), ReadArray<DroppedCharacter>(response, "dropped"),
            response.TryGetProperty("blocked", out var blocked) && blocked.ValueKind == JsonValueKind.True,
            ReadArray<BrailleDraft>(response, "drafts"), response.TryGetProperty("render_mode", out var mode) ? mode.GetString() : null);
    }

    public async Task<string> DecodeAsync(string cells, BrailleOptions settings, CancellationToken cancellationToken = default)
    {
        var result = await sidecar.InvokeAsync("decode", new { braille = cells, korean_grade1 = settings.KoreanGrade1, english = settings.EnglishDocument },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.GetProperty("text").GetString() ?? "";
    }

    public async Task<string> ReviewPriorityAsync(string cells, string source, BlockMetadata metadata, BrailleOptions settings,
        double? ocrConfidence = null, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, object?>
        {
            ["braille"] = cells, ["source"] = source, ["type"] = metadata.Type, ["korean_grade1"] = settings.KoreanGrade1
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

    public async Task<string> BuildBrfAsync(SmjManifest snapshot, CancellationToken cancellationToken = default)
    {
        var settings = snapshot.Options;
        var layout = snapshot.Layout;
        layout.Validate();
        var footer = string.IsNullOrEmpty(snapshot.FooterText) ? "" :
            (await TranslateElementAsync(snapshot.FooterText, new(Type: "header_footer"), settings, cancellationToken)).Cells;
        var pages = snapshot.Blocks.GroupBy(b => b.Page).Select(page => new
        {
            orig_page_no = page.Key,
            elements = page.Select(b => new
            {
                id = b.ServerId, type = b.Metadata.Type, heading_level = b.Metadata.HeadingLevel,
                text = b.Braille, breaks = b.HasManualBraille ? Array.Empty<int>() : b.Translation?.Breaks ?? []
            }).ToArray()
        }).ToArray();
        var result = await sidecar.InvokeAsync("brf", new
        {
            job = new
            {
                pages, footer_braille = footer, start_braille_page = layout.StartPage,
                options = new
                {
                    cols = layout.Columns, rows = layout.Lines,
                    page_row_on = layout.PageNumbers switch { "모든 페이지" => "every", "짝수 페이지" => "even", "홀수 페이지" => "odd", _ => "none" },
                    footer_align = layout.FooterAlign, wrap = settings.LineBreakUnit,
                    cover_pages = layout.CoverPages, orig_page_start = layout.SourcePageStart,
                    show_orig_page = layout.ShowSourcePageNumber, show_braille_page = layout.ShowBraillePageNumber, show_change_line = layout.ShowChangeLine
                }
            }
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var brf = result.GetProperty("brf").GetString() ?? throw new SidecarException("BRF 결과가 없습니다.");
        if (brf.Any(c => c > 127)) throw new SidecarException("BRF 응답에 ASCII가 아닌 문자가 있습니다.");
        return brf;
    }

    public async Task<string[][]> ReadBrfAsync(string brf, CancellationToken cancellationToken = default)
    {
        var result = await sidecar.InvokeAsync("read_brf", new { brf }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.GetProperty("pages").Deserialize<string[][]>() ?? [];
    }

    private static T[] ReadArray<T>(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        ? value.Deserialize<T[]>(Json) ?? [] : [];
}
