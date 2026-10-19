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
    [ObservableProperty] private BlockMetadata metadata = new();
    [ObservableProperty] private BrailleTranslation? translation;
    public ObservableCollection<LineMemo> Memos { get; } = [];
    [ObservableProperty] private string source = "";
    [ObservableProperty] private string braille = "";
    [ObservableProperty] private string memo = "";
    [ObservableProperty] private bool hasManualBraille;
    [ObservableProperty] private bool isBrailleLocked;
    [ObservableProperty] private string state = "점역 전";
    public int SourceRevision { get; private set; }
    public int BrailleRevision { get; private set; }
    public int MetadataRevision { get; private set; }
    // Navigation state is captured on explicit save; moving the caret is not a content edit.
    public int SourceCaret { get; set; }
    public int BrailleCaret { get; set; }

    partial void OnSourceChanged(string value) => SourceRevision++;
    partial void OnBrailleChanged(string value) => BrailleRevision++;
    partial void OnMetadataChanged(BlockMetadata value) => MetadataRevision++;
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
            { block.PropertyChanged -= BlockChanged; block.Memos.CollectionChanged -= MemosChanged; observedBlocks.Remove(block); }
            foreach (var block in Blocks.Where(b => !observedBlocks.Contains(b)))
            { block.PropertyChanged += BlockChanged; block.Memos.CollectionChanged += MemosChanged; observedBlocks.Add(block); }
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
    [ObservableProperty] private string status = "준비";
    [ObservableProperty] private PageLayout layout = new();
    [ObservableProperty] private BrailleOptions options = new();
    [ObservableProperty] private string footerText = "";
    public string? JobId { get; set; }
    public string? OwnerLoginId { get; set; }
    public string JobStatus { get; set; } = "COMPLETED";
    public Dictionary<int, string> PageStatuses { get; } = [];
    public Dictionary<int, OriginalPage> OriginalPages { get; } = [];
    public string WindowTitle => $"{(IsDirty ? "● " : "")}{Title} — 세모점";
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnLayoutChanged(PageLayout value) { ChangeVersion++; IsDirty = true; }
    partial void OnOptionsChanged(BrailleOptions value) { ChangeVersion++; IsDirty = true; }
    partial void OnFooterTextChanged(string value) { ChangeVersion++; IsDirty = true; }

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

public sealed record PageLayout(int Columns = 32, int Lines = 26, int StartPage = 1, string PageNumbers = "홀수 페이지", string Footer = "홀수 페이지",
    int CoverPages = 0, int SourcePageStart = 1, bool ShowSourcePageNumber = true, bool ShowBraillePageNumber = true,
    bool ShowChangeLine = true, string FooterAlign = "center", string EditScope = "all", bool AdvancedAi = false)
{
    public void Validate()
    {
        if (Columns is < 8 or > 100 || Lines is < 4 or > 100 || StartPage < 1 || SourcePageStart < 1 || CoverPages < 0
            || FooterAlign is not ("center" or "right") || EditScope is not ("all" or "page"))
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
