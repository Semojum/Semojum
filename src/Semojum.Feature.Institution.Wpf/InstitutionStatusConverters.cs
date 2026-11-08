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
        ("payment", var paidAt) => string.IsNullOrWhiteSpace(paidAt) ? "미납" : "완납",
        _ => value ?? "정보 없음"
    };
}

public sealed class InstitutionStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => FigmaDialogElements.Brush((parameter as string, value as string) switch
    {
        ("account", "ACTIVE") => "#0F7B0F", ("account", { } status) when status.EndsWith("요청", StringComparison.Ordinal) => "#9D5D00", ("notice", "ORG") => "#C42B1C",
        ("payment", var paidAt) => string.IsNullOrWhiteSpace(paidAt) ? "#C42B1C" : "#0F7B0F", _ => "#5F5F5F"
    });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InstitutionLastSeenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => InstitutionUsage.LastSeen(value as string, DateTimeOffset.Now);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>One line of the 소속 계정 table: a member account, or an open account-issue request shown in its place.</summary>
public sealed record InstitutionAccountRow(string LoginId, string? Alias, string Status, string? LastLoginAt, long? UsedCredits, string? Control,
    InstitutionAccount? Account = null, InstitutionRequest? Request = null)
{
    // 사용 is this month's usage, compared with the account's monthly limit; an older server only sends the cumulative total.
    public static InstitutionAccountRow Of(InstitutionAccount a) => new(a.LoginId, a.Alias, a.Status, a.LastLoginAt, a.MonthUsedCredits ?? a.UsedCredits,
        a.Self ? null : a.Status switch { "ACTIVE" => "잠금", "INACTIVE" => "잠금 해제", _ => null }, Account: a);
    public static InstitutionAccountRow Of(InstitutionRequest r) => new("발급 요청 중", r.Details?.AccountAlias ?? Field(r.Message, "별칭"),
        InstitutionUsage.InKorea(r.CreatedAt) is { } day ? day.ToString("MM-dd", CultureInfo.InvariantCulture) + " 요청" : "요청", null, null,
        r is { Type: "ACCOUNT_ISSUE", Status: "OPEN" } ? "요청 취소" : null, Request: r);
    // Requests sent before the request fields existed carry 「별칭: …」 as one line of the message.
    private static string? Field(string? message, string label) => message?.Split('\n').Select(l => l.Trim())
        .FirstOrDefault(l => l.StartsWith(label + ": ", StringComparison.Ordinal))?[(label.Length + 2)..];
}
