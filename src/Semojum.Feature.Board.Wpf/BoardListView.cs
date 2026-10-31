using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Semojum.Feature.Board.Presentation;

/// <summary>Board-owned list presentation; the host supplies its screen-specific style.</summary>
public sealed class BoardListView : DataGrid
{
    public BoardListView(Style gridStyle)
    {
        AutoGenerateColumns = false; IsReadOnly = true; CanUserAddRows = false;
        SelectionMode = DataGridSelectionMode.Single; RowHeight = 32; Style = gridStyle;
        var noticeText = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        noticeText.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14d));
        noticeText.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
        var regularPost = new DataTrigger { Binding = new Binding(nameof(BoardRow.NoticeId)), Value = null };
        regularPost.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12d));
        regularPost.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Normal));
        noticeText.Triggers.Add(regularPost);
        foreach (var (label, name, width) in new[] { ("번호", "Number", 70d), ("제목", "DisplayTitle", 570d), ("글쓴이", "Author", 120d), ("작성일", "CreatedAt", 100d), ("조회", "ViewCount", 80d) })
        {
            var column = new DataGridTextColumn { Header = label, Binding = new Binding(name), Width = width };
            if (name is "Number" or "DisplayTitle") column.ElementStyle = noticeText;
            Columns.Add(column);
        }
    }
}
