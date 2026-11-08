using System.Globalization;

namespace Semojum.Feature.Institution;

public static class InstitutionUsage
{
    public static string UsageRatio(long allocated, long used) => allocated <= 0 || used < 0 ? "정보 없음"
        : decimal.Round((decimal)used * 100 / allocated, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
    public static decimal? SixMonthAverage(IReadOnlyList<InstitutionMonthlyUsage> months)
    {
        if (months.Count != 6 || months.Any(m => m.Credits < 0)) return null;
        var parsed = new List<DateTime>(6);
        foreach (var month in months.OrderBy(m => m.Month, StringComparer.Ordinal))
        {
            if (!DateTime.TryParseExact(month.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
            parsed.Add(date);
        }
        parsed.Sort();
        if (parsed.Zip(parsed.Skip(1), (a, b) => (long)a.Year * 12 + a.Month + 1 == (long)b.Year * 12 + b.Month).Any(consecutive => !consecutive)) return null;
        return decimal.Round(months.Sum(m => (decimal)m.Credits) / 6m, 0, MidpointRounding.AwayFromZero);
    }
    public static string RemainingDays(string? date, DateOnly today)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)) return "정보 없음";
        var days = expires.DayNumber - today.DayNumber;
        return days > 0 ? $"{days:N0}일 남음" : days == 0 ? "오늘 만료" : $"{-(long)days:N0}일 경과";
    }
    private static TimeZoneInfo Korea => TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
    /// <summary>Server times are UTC; a bare date is read as that calendar day.</summary>
    public static DateTimeOffset? InKorea(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? TimeZoneInfo.ConvertTime(at, Korea) : null;
    /// <summary>Table form 「오늘 09:12 / 어제 / 07-22 / —」; the account detail keeps the time for yesterday too (「어제 17:02」).</summary>
    public static string LastSeen(string? value, DateTimeOffset now, bool withTime = false)
    {
        if (InKorea(value) is not { } at) return "—";
        var today = TimeZoneInfo.ConvertTime(now, Korea); var days = (today.Date - at.Date).Days;
        var time = value!.Contains('T') ? at.ToString(" HH:mm", CultureInfo.InvariantCulture) : "";
        return days switch { 0 => "오늘" + time, 1 => withTime ? "어제" + time : "어제", _ => at.ToString(at.Year == today.Year ? "MM-dd" : "yyyy-MM-dd", CultureInfo.InvariantCulture) };
    }
}
