using System.Text.Json;

namespace Semojum.Core.Integration;

public sealed record AppVersionInfo(Version Latest, Version Minimum, Uri? DownloadUrl, string ReleaseNotes)
{
    public bool Required(Version current) => current < Minimum;
    public bool Available(Version current) => current < Latest;
    public static AppVersionInfo? Read(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null) return null;
        string? Field(string name) => result.ValueKind == JsonValueKind.Object && result.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (!Version.TryParse(Field("latestVersion"), out var latest)
            || !Version.TryParse(Field("minSupportedVersion"), out var minimum) || latest < minimum)
            throw new InvalidDataException("버전 정보가 올바르지 않습니다.");
        Uri? uri = null;
        if (result.TryGetProperty("downloadUrl", out var download) && download.ValueKind != JsonValueKind.Null
            && (download.ValueKind != JsonValueKind.String || !Uri.TryCreate(download.GetString(), UriKind.Absolute, out uri)
                || uri.Scheme != "https" || uri.UserInfo.Length != 0))
            throw new InvalidDataException("업데이트 다운로드 주소가 올바르지 않습니다.");
        return new(latest, minimum, uri, Field("releaseNotes") ?? "");
    }
}
