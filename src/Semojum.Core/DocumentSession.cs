using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Collections.Specialized;

namespace Semojum.Core;

public sealed partial class EditorBlock : ObservableObject
{
    public int Id { get; init; }
    public int Page { get; init; }
    public string Label => $"{Id:00}  {Heading}";
    public string Heading { get; init; } = "";
    public string? ServerId { get; set; }
    // Stable across snapshots, retries and SMJ transfers, including before the first server save.
    public string ClientId { get; init; } = "app_" + Guid.NewGuid().ToString("N");
    [ObservableProperty] private BlockMetadata metadata = new();
    [ObservableProperty] private BrailleTranslation? translation;
    public ObservableCollection<LineMemo> Memos { get; } = [];
    public ObservableCollection<ReviewMark> ReviewMarks { get; } = [];
    [ObservableProperty] private string source = "";
    [ObservableProperty] private string braille = "";
    [ObservableProperty] private string memo = "";
    [ObservableProperty] private bool hasManualBraille;
    [ObservableProperty] private bool isBrailleLocked;
    [ObservableProperty] private string state = "점역 전";
    public int SourceRevision { get; private set; }
    public int BrailleRevision { get; private set; }
    public int MetadataRevision { get; private set; }
    // Advances even when newly translated cells compare equal and no property change fires.
    public int BrailleConfirmationRevision { get; private set; }
    // Navigation state is captured on explicit save; moving the caret is not a content edit.
    public int SourceCaret { get; set; }
    public int BrailleCaret { get; set; }

    partial void OnSourceChanging(string? oldValue, string newValue)
    {
        ReanchorReviews(ReviewPanel.Text, oldValue ?? "", newValue);
        // A human's braille confirmation also refers to the corresponding source.
        // Until rechecked, identical cells cannot prove the changed source is correct.
        InvalidateBrailleConfirmations();
    }
    partial void OnBrailleChanging(string? oldValue, string newValue) => ReanchorReviews(ReviewPanel.Braille, oldValue ?? "", newValue);
    partial void OnSourceChanged(string value) => SourceRevision++;
    partial void OnBrailleChanged(string value) => BrailleRevision++;
    partial void OnMetadataChanged(BlockMetadata value) => MetadataRevision++;
    private void ReanchorReviews(ReviewPanel panel, string before, string after)
    {
        for (var i = 0; i < ReviewMarks.Count; i++)
        {
            var mark = ReviewMarks[i];
            if (mark.Panel != panel) continue;
            var updated = ReviewAnchors.Reanchor(mark, before, after);
            if (updated != mark) ReviewMarks[i] = updated;
        }
    }
    public void InvalidateBrailleConfirmations()
    {
        BrailleConfirmationRevision++;
        for (var i = 0; i < ReviewMarks.Count; i++)
        {
            var mark = ReviewMarks[i];
            if (mark.Panel == ReviewPanel.Braille && mark.Kind == ReviewKind.HumanConfirmed && !mark.IsStale)
                ReviewMarks[i] = mark with { Length = 0, IsStale = true };
        }
    }
    public ReviewMark AddReviewMark(ReviewKind kind, ReviewPanel panel, int start, int length)
    {
        var text = panel == ReviewPanel.Text ? Source : Braille;
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(panel) || start < 0 || length <= 0 || start > text.Length || length > text.Length - start)
            throw new ArgumentOutOfRangeException(nameof(start), "검토할 범위를 선택해 주세요.");
        var mark = new ReviewMark(Guid.NewGuid().ToString("N"), kind, panel, start, length, text.Substring(start, length));
        if (!ReviewAnchors.Valid(mark, Source, Braille)) throw new ArgumentException("문자를 나누지 않는 검토 범위를 선택해 주세요.");
        ReviewMarks.Add(mark);
        return mark;
    }
    public void EditBraille(string value)
    {
        if (IsBrailleLocked || Braille == value) return;
        Braille = value;
        HasManualBraille = true;
        Translation = null;
        State = "직접 수정";
    }
}

public sealed partial class DocumentSession : ObservableObject
{
    private readonly HashSet<EditorBlock> observedBlocks = [];
    public DocumentSession()
    {
        Blocks.CollectionChanged += (_, _) =>
        {
            foreach (var block in observedBlocks.Where(b => !Blocks.Contains(b)).ToArray())
            { block.PropertyChanged -= BlockChanged; block.Memos.CollectionChanged -= MemosChanged; block.ReviewMarks.CollectionChanged -= MemosChanged; observedBlocks.Remove(block); }
            foreach (var block in Blocks.Where(b => !observedBlocks.Contains(b)))
            { block.PropertyChanged += BlockChanged; block.Memos.CollectionChanged += MemosChanged; block.ReviewMarks.CollectionChanged += MemosChanged; observedBlocks.Add(block); }
            MarkChanged();
        };
    }
    public void MarkChanged() { ChangeVersion++; IsDirty = true; }
    private void MemosChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkChanged();
    private void BlockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorBlock.Source) or nameof(EditorBlock.Braille) or nameof(EditorBlock.Memo)
            or nameof(EditorBlock.HasManualBraille) or nameof(EditorBlock.Metadata) or nameof(EditorBlock.Translation)) MarkChanged();
    }
    public ObservableCollection<EditorBlock> Blocks { get; } = [];
    public Dictionary<string, byte[]> OriginalFiles { get; } = [];
    public long ChangeVersion { get; private set; }
    [ObservableProperty] private EditorBlock? activeBlock;
    [ObservableProperty] private string title = "제목 없음.smj";
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private bool isReadOnly;
    [ObservableProperty] private string status = "준비";
    [ObservableProperty] private ConversionProgress? progress;
    [ObservableProperty] private string creditNotice = "";
    [ObservableProperty] private string errorNotice = "";
    [ObservableProperty] private PageLayout layout = new();
    [ObservableProperty] private BrailleOptions options = new();
    [ObservableProperty] private string footerText = "";
    [ObservableProperty] private FooterRange[] footerRanges = [];
    public string? JobId { get; set; }
    public string? OwnerLoginId { get; set; }
    public bool? IsTextJob { get; set; }
    public string JobStatus { get; set; } = "COMPLETED";
    public bool CancellationRequested { get; set; }
    public Dictionary<int, string> PageStatuses { get; } = [];
    public Dictionary<int, OriginalPage> OriginalPages { get; } = [];
    /// <summary>Blocks the server is known to hold per page (last read or last confirmed save). Drives deletion and braille-panel planning.</summary>
    public Dictionary<int, ServerBlockRecord[]> ServerPages { get; } = [];
    public Dictionary<int, PendingServerPage> PendingServerPages { get; } = [];
    private PendingRestart? pendingRestart;
    public PendingRestart? PendingRestart
    {
        get => pendingRestart;
        set
        {
            if (Equals(pendingRestart, value)) return;
            pendingRestart = value;
            OnPropertyChanged(nameof(PendingRestart)); OnPropertyChanged(nameof(HasPendingRestart));
            MarkChanged();
        }
    }
    public bool HasPendingRestart => pendingRestart is not null;
    public bool HasPendingServerPages => PendingServerPages.Count != 0;
    public void SetPendingServerPage(PendingServerPage page)
    {
        PendingServerPages[page.Page] = page;
        OnPropertyChanged(nameof(HasPendingServerPages));
        MarkChanged();
    }
    public void ClearPendingServerPages()
    {
        if (PendingServerPages.Count == 0) return;
        PendingServerPages.Clear();
        OnPropertyChanged(nameof(HasPendingServerPages));
        MarkChanged();
    }
    /// <summary>Server quality report per page. Replaced on every page read; kept in .smj for offline review.</summary>
    public Dictionary<int, QualityIssue[]> QualityReports { get; } = [];
    /// <summary>Page average OCR confidence measured by the AI (BE B-10). Null when the AI sent none; never estimated locally.</summary>
    public Dictionary<int, double?> OcrConfidence { get; } = [];
    /// <summary>Last "kept locally only" notice shown for this document, so repeated saves do not repeat the same dialog. Not persisted.</summary>
    public string? LastSyncNotice { get; set; }
    public string WindowTitle => $"{(IsDirty ? "● " : "")}{Title} — 세모점";
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnLayoutChanged(PageLayout value) { ChangeVersion++; IsDirty = true; }
    partial void OnOptionsChanged(BrailleOptions value) { ChangeVersion++; IsDirty = true; }
    partial void OnFooterTextChanged(string value) { ChangeVersion++; IsDirty = true; }
    partial void OnFooterRangesChanged(FooterRange[] value) { ChangeVersion++; IsDirty = true; }

    public void Add(EditorBlock block)
    {
        Blocks.Add(block);
        ActiveBlock ??= block;
    }

    public void Move(int delta)
    {
        if (Blocks.Count == 0) return;
        var index = ActiveBlock is null ? 0 : Blocks.IndexOf(ActiveBlock);
        ActiveBlock = Blocks[Math.Clamp(index + delta, 0, Blocks.Count - 1)];
    }

    public static DocumentSession Sample()
    {
        var session = new DocumentSession { Title = "2026_수능특강_사회문화.smj", Status = "샘플 문서" };
        session.Add(new EditorBlock { Id = 1, Page = 12, Heading = "한국지리 · 기후 특성", Source = SampleData.Sources[0], Braille = SampleData.Braille[0] });
        session.Add(new EditorBlock { Id = 2, Page = 12, Heading = "선택지 · 네 지역 비교", Source = SampleData.Sources[1], Braille = SampleData.Braille[1] });
        session.Add(new EditorBlock { Id = 3, Page = 13, Heading = "자료 해설 · 연교차", Source = SampleData.Sources[2], Braille = SampleData.Braille[2] });
        session.IsDirty = false;
        return session;
    }
}

public sealed record OriginalPage(string Type, string? AssetName, string[]? Lines = null, int Width = 0, int Height = 0);
/// <summary>A block the server holds for a page. <see cref="HasBraille"/> is whether a braille element with the same id exists there.</summary>
public sealed record ServerBlockRecord(string Id, bool HasBraille);
/// <summary>Server quality report entry. <see cref="Severity"/> is "error" (critical_errors) or "review" (review_flags).</summary>
public sealed record QualityIssue(string Severity, string Type, string? ElementId, string Message)
{
    public string SeverityLabel => Severity == "error" ? "오류" : "검토";
}
/// <summary>
/// Page counts are disjoint: Succeeded (COMPLETED/NEEDS_REVIEW) + Failed (BLOCKED/FAILED/CANCELED) = Finished,
/// and Finished + Running + Pending = Total. The server's completedPages already includes failed pages.
/// </summary>
public sealed record ConversionProgress(int Total, int Succeeded, int Failed, int Running, string Estimate)
{
    public string CompactLabel => $"{Total}쪽 중 {Finished}쪽 · {Estimate}";
    public int Finished => Math.Min(Total, Succeeded + Failed);
    public int Remaining => Math.Max(0, Total - Finished);
    public string Label => $"{Total}쪽 중 {Finished}쪽 처리 · 성공 {Succeeded} · 확인 필요 {Failed}"
        + (Running > 0 ? $" · 변환 중 {Running}" : "") + (Estimate.Length == 0 ? "" : " · " + Estimate);
}

public sealed record PageLayout(int Columns = 32, int Lines = 26, int StartPage = 1, string PageNumbers = "홀수 페이지", string Footer = "홀수 페이지",
    int CoverPages = 0, int SourcePageStart = 1, bool ShowSourcePageNumber = true, bool ShowBraillePageNumber = true,
    bool ShowChangeLine = true, string FooterAlign = "center", string EditScope = "all", bool AdvancedAi = false, bool PadToEven = false, PageNumberRange[]? NumberRanges = null)
{
    public void Validate()
    {
        PageNumberRange.Validate(NumberRanges);
        if (Columns is < 8 or > 100 || Lines is < 4 or > 100 || StartPage < 1 || SourcePageStart < 1 || CoverPages < 0
            || FooterAlign is not ("center" or "right" or "left") || EditScope is not ("all" or "page"))
            throw new ArgumentException("줄당 칸 수 8–100, 페이지당 줄 수 4–100, 시작 페이지 1 이상으로 입력해 주세요.");
    }

    public IReadOnlyList<string> Wrap(string text)
    {
        Validate();
        var lines = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0) { lines.Add(""); continue; }
            for (var i = 0; i < line.Length; i += Columns) lines.Add(line.Substring(i, Math.Min(Columns, line.Length - i)));
        }
        return lines;
    }
}
