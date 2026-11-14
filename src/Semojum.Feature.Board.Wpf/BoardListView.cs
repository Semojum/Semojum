using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Media;

namespace Semojum.Feature.Board.Presentation;

/// <summary>Board-owned list presentation; the host supplies its screen-specific style.</summary>
public sealed class BoardListView : DataGrid
{
    public BoardListView(Style gridStyle)
    {
        AutoGenerateColumns = false; IsReadOnly = true; CanUserAddRows = false;
        SelectionMode = DataGridSelectionMode.Single; RowHeight = 32; Style = gridStyle;
        AlternationCount = int.MaxValue;
        var noticeBackground = new MultiBinding { Converter = new NoticeBackgroundConverter() };
        noticeBackground.Bindings.Add(new Binding(nameof(BoardRow.NoticeId)));
        noticeBackground.Bindings.Add(new Binding { Path = new PropertyPath("(0)", ItemsControl.AlternationIndexProperty), RelativeSource = RelativeSource.Self });
        RowStyle = new Style(typeof(DataGridRow));
        RowStyle.Setters.Add(new Setter(Control.BackgroundProperty, noticeBackground));
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
    private sealed class NoticeBackgroundConverter : IMultiValueConverter
    {
        private static readonly Brush Highlight = new SolidColorBrush(Color.FromRgb(239, 246, 252));
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => values is [string { Length: > 0 }, int index] && index < 2 ? Highlight : Brushes.White;
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }}
