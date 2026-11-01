using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Semojum.Feature.Board;

/// <summary>The four board sidebar lists (Figma D8 §8-1): 전체 글 · 공지 · 내 글 · 답변 받은 글.</summary>
public enum BoardListKind { All, Notices, Mine, Answered }

/// <summary>Owns the loaded board list. Failed or cancelled requests leave the last successful view intact.</summary>
public sealed class BoardListViewModel(IBoardListSource source, bool initiallyMine = false) : INotifyPropertyChanged
{
    private const int PageSize = 10;
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Page { get; private set; } = 1;
    public int TotalCount { get; private set; }
    public BoardListKind Kind { get; private set; } = initiallyMine ? BoardListKind.Mine : BoardListKind.All;
    public bool Mine => Kind == BoardListKind.Mine;
    public bool IsLoading { get; private set; }
    public IReadOnlyList<BoardRow> Rows { get; private set; } = Array.Empty<BoardRow>();
    public int TotalPages => Math.Max(1, Math.Max(0, TotalCount) / PageSize + (TotalCount > 0 && TotalCount % PageSize != 0 ? 1 : 0));

    public Task LoadAsync(int page, bool mine, string field, string keyword, CancellationToken cancellationToken = default)
        => LoadAsync(page, mine ? BoardListKind.Mine : BoardListKind.All, field, keyword, cancellationToken);

    public async Task LoadAsync(int page, BoardListKind kind, string field, string keyword, CancellationToken cancellationToken = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (IsLoading) throw new InvalidOperationException("게시판 목록을 불러오는 중입니다.");
        IsLoading = true; Changed(nameof(IsLoading));
        try
        {
            var (total, rows) = kind switch
            {
                BoardListKind.Notices => Slice(await NoticesAsync(field, keyword, cancellationToken), page),
                BoardListKind.Answered => Slice(await AnsweredAsync(field, keyword, cancellationToken), page),
                _ => await ServerPageAsync(page, kind == BoardListKind.Mine, field, keyword, cancellationToken)
            };
            cancellationToken.ThrowIfCancellationRequested();
            Page = page; Kind = kind; TotalCount = total; Rows = rows;
            Changed(nameof(Page)); Changed(nameof(Kind)); Changed(nameof(Mine)); Changed(nameof(TotalCount)); Changed(nameof(TotalPages)); Changed(nameof(Rows));
        }
        finally { IsLoading = false; Changed(nameof(IsLoading)); }
    }

    private async Task<(int Total, BoardRow[] Rows)> ServerPageAsync(int page, bool mine, string field, string keyword, CancellationToken cancellationToken)
    {
        var data = await source.ListAsync(page, field, keyword, mine, cancellationToken);
        return (data.TotalCount, data.Notices.Concat(data.Items).ToArray());
    }
    // The pinned notices come in result.notices of the first list page, which is empty when searching, so the
    // search applies here: 글쓴이 to the author and the other ranges to the title (the list has no notice body).
    private async Task<BoardRow[]> NoticesAsync(string field, string keyword, CancellationToken cancellationToken)
    {
        var notices = (await source.ListAsync(1, "TITLE_CONTENT", "", false, cancellationToken)).Notices ?? [];
        var term = keyword.Trim();
        return term.Length == 0 ? notices
            : notices.Where(n => ((field == "AUTHOR" ? n.Author : n.Title) ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    // Reads every page of my posts and keeps those with an operator answer. The notification list holds only
    // answers the operator chose to announce, so it cannot stand in for this list.
    private async Task<BoardRow[]> AnsweredAsync(string field, string keyword, CancellationToken cancellationToken)
    {
        var answered = new List<BoardRow>();
        for (var page = 1; ; page++)
        {
            var data = await source.ListAsync(page, field, keyword, true, cancellationToken);
            var items = data.Items ?? [];
            answered.AddRange(items.Where(row => row.AnswerCount > 0));
            if (items.Length == 0 || page * PageSize >= data.TotalCount) return answered.ToArray();
        }
    }
    private static (int Total, BoardRow[] Rows) Slice(BoardRow[] rows, int page) => (rows.Length, rows.Skip((page - 1) * PageSize).Take(PageSize).ToArray());

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
