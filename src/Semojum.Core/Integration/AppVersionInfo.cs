using System.Text.Json;

namespace Semojum.Core.Integration;

public sealed record AppVersionInfo(Version Latest, Version Minimum, Uri DownloadUrl, string ReleaseNotes)
{
    public bool Required(Version current) => current < Minimum;
    public bool Available(Version current) => current < Latest;
    public static AppVersionInfo? Read(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null) return null;
        if (!Version.TryParse(result.GetProperty("latestVersion").GetString(), out var latest)
            || !Version.TryParse(result.GetProperty("minSupportedVersion").GetString(), out var minimum)
            || !Uri.TryCreate(result.GetProperty("downloadUrl").GetString(), UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.UserInfo.Length != 0 || latest < minimum)
            throw new InvalidDataException("버전 정보가 올바르지 않습니다.");
        return new(latest, minimum, uri, result.TryGetProperty("releaseNotes", out var notes) ? notes.GetString() ?? "" : "");
    }
}
