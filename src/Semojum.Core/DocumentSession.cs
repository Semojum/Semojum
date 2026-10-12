using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Semojum.Core;

public sealed partial class EditorBlock : ObservableObject
{
    public int Id { get; init; }
    public int Page { get; init; }
    public string Label => $"{Id:00}  {Heading}";
    public string Heading { get; init; } = "";
    [ObservableProperty] private string source = "";
    [ObservableProperty] private string braille = "";
    [ObservableProperty] private string memo = "";
    [ObservableProperty] private bool hasManualBraille;
    [ObservableProperty] private bool isBrailleLocked;
    [ObservableProperty] private string state = "샘플 점역";
    public int SourceRevision { get; private set; }
    public int BrailleRevision { get; private set; }

    partial void OnSourceChanged(string value) => SourceRevision++;
    partial void OnBrailleChanged(string value) => BrailleRevision++;
    public void EditBraille(string value)
    {
        if (IsBrailleLocked || Braille == value) return;
        Braille = value;
        HasManualBraille = true;
        State = "직접 수정";
    }
}

public sealed partial class DocumentSession : ObservableObject
{
    public ObservableCollection<EditorBlock> Blocks { get; } = [];
    public long ChangeVersion { get; private set; }
    [ObservableProperty] private EditorBlock? activeBlock;
    [ObservableProperty] private string title = "2026_수능특강_사회문화.mock.smi";
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string status = "준비 · 샘플 문서";
    [ObservableProperty] private PageLayout layout = new();
    public string WindowTitle => $"{(IsDirty ? "● " : "")}{Title} — 세모점";
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnLayoutChanged(PageLayout value) { ChangeVersion++; IsDirty = true; }

    public void Add(EditorBlock block)
    {
        Blocks.Add(block);
        block.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditorBlock.Source) or nameof(EditorBlock.Braille)
                or nameof(EditorBlock.Memo) or nameof(EditorBlock.HasManualBraille)) { ChangeVersion++; IsDirty = true; }
        };
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
        var session = new DocumentSession();
        session.Add(new EditorBlock { Id = 1, Page = 12, Heading = "한국지리 · 기후 특성", Source = SampleData.Sources[0], Braille = SampleData.Braille[0] });
        session.Add(new EditorBlock { Id = 2, Page = 12, Heading = "선택지 · 네 지역 비교", Source = SampleData.Sources[1], Braille = SampleData.Braille[1] });
        session.Add(new EditorBlock { Id = 3, Page = 13, Heading = "자료 해설 · 연교차", Source = SampleData.Sources[2], Braille = SampleData.Braille[2] });
        session.IsDirty = false;
        return session;
    }
}

public sealed record PageLayout(int Columns = 32, int Lines = 25, int StartPage = 1, string PageNumbers = "홀수 페이지", string Footer = "홀수 페이지")
{
    public void Validate()
    {
        if (Columns is < 8 or > 80 || Lines is < 8 or > 60 || StartPage < 1)
            throw new ArgumentException("줄당 칸 수 8–80, 페이지당 줄 수 8–60, 시작 페이지 1 이상으로 입력해 주세요.");
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
