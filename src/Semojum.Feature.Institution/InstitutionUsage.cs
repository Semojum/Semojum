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
}
