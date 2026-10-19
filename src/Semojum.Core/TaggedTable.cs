using System.Text.RegularExpressions;

namespace Semojum.Core;
public static class TaggedTable
{
    public static string[][] Read(string source)
    {
        var table = Regex.Match(source.Trim(), @"\A<!표>\s*(.*?)\s*<!/표>\z", RegexOptions.Singleline);
        if (!table.Success) throw new ArgumentException("표 구조 태그를 찾을 수 없습니다.");
        var rows = Regex.Matches(table.Groups[1].Value, @"<!행>(.*?)<!/행>", RegexOptions.Singleline);
        if (rows.Count == 0 || Regex.Replace(table.Groups[1].Value, @"<!행>.*?<!/행>", "", RegexOptions.Singleline).Trim().Length != 0) throw new ArgumentException("표의 행 구조를 확인해 주세요.");
        return rows.Select(row =>
        {
            var text = row.Groups[1].Value.Trim();
            if (!text.StartsWith("<!칸>")) throw new ArgumentException("각 행에는 칸이 있어야 합니다.");
            var cells = text[4..].Replace("<!/칸>", "").Split("<!칸>").Select(c => c.Trim()).ToArray();
            if (cells.Any(c => Regex.IsMatch(c, @"<!/?(?:표|행|칸)>"))) throw new ArgumentException("표 안에 중첩된 행·칸 태그가 있습니다.");
            return cells;
        }).ToArray();
    }
    public static string Write(IEnumerable<IEnumerable<string>> rows)
    {
        var data = rows.Select(r => r.ToArray()).ToArray();
        if (data.Length is 0 or > 100 || data.Any(r => r.Length is 0 or > 100 || r.Any(c => Regex.IsMatch(c, @"<!/?(?:표|행|칸)>"))))
            throw new ArgumentException("행·열은 1–100개이며, 셀 안에 표 구조 태그를 넣을 수 없습니다.");
        return "<!표>\n" + string.Join("\n", data.Select(r => "<!행>" + string.Join("", r.Select(c => "<!칸>" + c)) + "<!/행>")) + "\n<!/표>";
    }
}
