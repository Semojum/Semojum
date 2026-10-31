using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Semojum.Feature.Board;

/// <summary>Owns the loaded board list. Failed or cancelled requests leave the last successful view intact.</summary>
public sealed class BoardListViewModel(IBoardListSource source, bool initiallyMine = false) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Page { get; private set; } = 1;
    public int TotalCount { get; private set; }
    public bool Mine { get; private set; } = initiallyMine;
    public bool IsLoading { get; private set; }
    public IReadOnlyList<BoardRow> Rows { get; private set; } = Array.Empty<BoardRow>();
    public int TotalPages => Math.Max(1, Math.Max(0, TotalCount) / 10 + (TotalCount > 0 && TotalCount % 10 != 0 ? 1 : 0));

    public async Task LoadAsync(int page, bool mine, string field, string keyword, CancellationToken cancellationToken = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (IsLoading) throw new InvalidOperationException("게시판 목록을 불러오는 중입니다.");
        IsLoading = true; Changed(nameof(IsLoading));
        try
        {
            var data = await source.ListAsync(page, field, keyword, mine, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var rows = data.Notices.Concat(data.Items).ToArray();
            Page = page; Mine = mine; TotalCount = data.TotalCount; Rows = rows;
            Changed(nameof(Page)); Changed(nameof(Mine)); Changed(nameof(TotalCount)); Changed(nameof(TotalPages)); Changed(nameof(Rows));
        }
        finally { IsLoading = false; Changed(nameof(IsLoading)); }
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
