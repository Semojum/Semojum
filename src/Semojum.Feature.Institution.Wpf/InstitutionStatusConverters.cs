using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

using Semojum.Desktop.Dialogs;

namespace Semojum.Feature.Institution.Presentation;

public sealed class InstitutionReceiptVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasReceipt = !string.IsNullOrWhiteSpace(value as string);
        return hasReceipt != string.Equals(parameter as string, "missing", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InstitutionStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Label(value as string, parameter as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    public static string Label(string? value, string? kind) => (kind, value) switch
    {
        ("account", "ACTIVE") => "활성",
        ("account", "INACTIVE") => "잠김",
        ("notice", "ORG") => "우리 기관",
        ("notice", "ALL") => "전체",
        ("invoice", "PENDING") => "발행 대기",
        ("invoice", "ISSUED") => "발행 완료",
        _ => value ?? "정보 없음"
    };
}

public sealed class InstitutionStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => FigmaDialogElements.Brush((parameter as string, value as string) switch
    { ("account", "ACTIVE") => "#0F7B0F", ("notice", "ORG") => "#C42B1C", _ => "#5F5F5F" });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
