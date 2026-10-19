using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Semojum.Core;

public sealed record LineMemo(string LocalId, long? ServerId, string Panel, int LineIndex, string LineText, string Body, DateTimeOffset CreatedAt, bool Resolved = false);
public sealed record SmjBlock(int Id, string? ServerId, int Page, string Heading, string Source, string Braille,
    string Memo, bool HasManualBraille, BlockMetadata Metadata, BrailleTranslation? Translation, LineMemo[] Memos,
    int SourceCaret = 0, int BrailleCaret = 0);
public sealed record OriginalAsset(string Entry, string FileName, long Length, string Sha256);
public sealed record SmjManifest(string Format, int Version, string? JobId, string? OwnerLoginId, string JobStatus,
    PageLayout Layout, BrailleOptions Options, string FooterText, int? ActiveBlockId, SmjBlock[] Blocks, OriginalAsset[] Originals,
    Dictionary<int, string>? PageStatuses = null, Dictionary<int, OriginalPage>? OriginalPages = null, FooterRange[]? FooterRanges = null);
public sealed record SmjSnapshot(SmjManifest Manifest, IReadOnlyDictionary<string, byte[]> Assets);

/// <summary>Versioned, self-contained ZIP. All reading happens in memory; archive paths are never extracted.</summary>
public static class SmjFile
{
    public const string Format = "semojum-work";
    public const int Version = 1;
    public const long MaximumExpandedBytes = 512L * 1024 * 1024;
    private const long MaximumManifestBytes = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SmjSnapshot Snapshot(DocumentSession session, bool includeOriginals = true)
    {
        var assets = new Dictionary<string, byte[]>();
        var originals = (includeOriginals ? session.OriginalFiles : new Dictionary<string, byte[]>()).Select((pair, index) =>
        {
            var entry = $"originals/{index:D4}";
            var bytes = pair.Value.ToArray(); assets.Add(entry, bytes);
            return new OriginalAsset(entry, pair.Key, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
        }).ToArray();
        var blocks = session.Blocks.Select(b => new SmjBlock(b.Id, b.ServerId, b.Page, b.Heading, b.Source, b.Braille,
            b.Memo, b.HasManualBraille, b.Metadata, b.Translation, b.Memos.ToArray(), b.SourceCaret, b.BrailleCaret)).ToArray();
        var manifest = new SmjManifest(Format, Version, session.JobId, session.OwnerLoginId, session.JobStatus,
            session.Layout, session.Options, session.FooterText, session.ActiveBlock?.Id, blocks, originals,
            new(session.PageStatuses), new(session.OriginalPages), session.FooterRanges.ToArray());
        // Detach nested arrays from live editor/engine state before awaiting disk or network I/O.
        manifest = JsonSerializer.Deserialize<SmjManifest>(JsonSerializer.SerializeToUtf8Bytes(manifest, Json), Json)!;
        Validate(manifest);
        return new(manifest, assets);
    }

    public static async Task SaveAsync(string path, SmjSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Validate(snapshot.Manifest);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(snapshot.Manifest, Json);
        if (manifest.LongLength > MaximumManifestBytes || manifest.LongLength + snapshot.Manifest.Originals.Sum(a => a.Length) > MaximumExpandedBytes)
            throw new InvalidDataException("작업 정보가 .smj 크기 한도를 초과했습니다.");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteEntryAsync(zip, "manifest.json", manifest, cancellationToken);
                    foreach (var asset in snapshot.Manifest.Originals)
                    {
                        if (!snapshot.Assets.TryGetValue(asset.Entry, out var bytes) || bytes.LongLength != asset.Length
                            || Convert.ToHexString(SHA256.HashData(bytes)) != asset.Sha256)
                            throw new InvalidDataException("원본 파일이 스냅샷과 일치하지 않습니다.");
                        await WriteEntryAsync(zip, asset.Entry, bytes, cancellationToken);
                    }
                }
                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static async Task<DocumentSession> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        if (zip.Entries.Count > 10002 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            throw new InvalidDataException("작업 파일 항목이 중복되거나 너무 많습니다.");
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.Length > MaximumExpandedBytes - expanded) throw new InvalidDataException("작업 파일의 압축 해제 크기 한도를 초과했습니다.");
            expanded += entry.Length;
        }
        var metadata = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("세모점 작업 정보가 없습니다.");
        if (metadata.Length > MaximumManifestBytes) throw new InvalidDataException("작업 정보가 너무 큽니다.");
        SmjManifest manifest;
        await using (var stream = metadata.Open())
        {
            manifest = JsonSerializer.Deserialize<SmjManifest>(await ReadBoundedAsync(stream, metadata.Length, cancellationToken), Json)
                ?? throw new InvalidDataException("작업 정보를 읽을 수 없습니다.");
        }
        Validate(manifest);
        if (zip.Entries.Count != manifest.Originals.Length + 1) throw new InvalidDataException("작업 파일에 명시되지 않은 항목이 있습니다.");
        var model = Restore(manifest, Path.GetFileName(path));
        foreach (var original in manifest.Originals)
        {
            var entry = zip.GetEntry(original.Entry) ?? throw new InvalidDataException("원본 파일이 누락되었습니다.");
            if (entry.Length != original.Length) throw new InvalidDataException("원본 파일 크기가 일치하지 않습니다.");
            await using var stream = entry.Open();
            var bytes = await ReadBoundedAsync(stream, original.Length, cancellationToken);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != original.Sha256) throw new InvalidDataException("원본 파일 검증에 실패했습니다.");
            model.OriginalFiles.Add(original.FileName, bytes);
        }
        model.IsDirty = false;
        return model;
    }

    private static DocumentSession Restore(SmjManifest manifest, string title)
    {
        var model = new DocumentSession
        {
            Title = title, JobId = manifest.JobId, OwnerLoginId = manifest.OwnerLoginId, JobStatus = manifest.JobStatus,
            Layout = manifest.Layout, Options = manifest.Options, FooterText = manifest.FooterText, FooterRanges = manifest.FooterRanges ?? []
        };
        foreach (var pair in manifest.PageStatuses ?? []) model.PageStatuses.Add(pair.Key, pair.Value);
        foreach (var pair in manifest.OriginalPages ?? []) model.OriginalPages.Add(pair.Key, pair.Value);
        foreach (var b in manifest.Blocks)
        {
            var block = new EditorBlock
            {
                Id = b.Id, ServerId = b.ServerId, Page = b.Page, Heading = b.Heading, Source = b.Source, Braille = b.Braille,
                Memo = b.Memo, HasManualBraille = b.HasManualBraille, Metadata = b.Metadata, Translation = b.Translation,
                SourceCaret = Math.Clamp(b.SourceCaret, 0, b.Source.Length), BrailleCaret = Math.Clamp(b.BrailleCaret, 0, b.Braille.Length),
                State = b.HasManualBraille ? "직접 수정" : b.Translation?.NeedsReview == true ? "검토 필요" : "저장된 점자"
            };
            foreach (var memo in b.Memos) block.Memos.Add(memo);
            model.Add(block);
        }
        model.ActiveBlock = model.Blocks.FirstOrDefault(b => b.Id == manifest.ActiveBlockId) ?? model.Blocks[0];
        return model;
    }

    private static void Validate(SmjManifest m)
    {
        if (m.Format != Format || m.Version != Version) throw new InvalidDataException("지원하지 않는 .smj 형식 또는 버전입니다.");
        if (m.Layout is null || m.Options is null || m.FooterText is null || m.Blocks is not { Length: > 0 } || m.Originals is null)
            throw new InvalidDataException("작업 필수 정보가 없습니다.");
        m.Layout.Validate();
        FooterRange.Validate(m.FooterRanges ?? []);
        if (m.Options.LineBreakUnit is not ("syllable" or "word")) throw new InvalidDataException("알 수 없는 줄바꿈 옵션입니다.");
        if (m.Blocks.Any(b => b is null || b.Id < 1 || b.Page < 1 || b.Heading is null || b.Source is null || b.Braille is null
            || b.Memo is null || b.Metadata is null || b.Memos is null || b.Memos.Any(x => x is null || x.Panel is not ("TEXT" or "BRAILLE") || x.LineIndex < 0 || x.Body is null))
            || m.Blocks.Select(b => b.Id).Distinct().Count() != m.Blocks.Length)
            throw new InvalidDataException("문서 블록 또는 메모가 올바르지 않습니다.");
        long total = 0;
        foreach (var asset in m.Originals)
        {
            if (asset is null || asset.Entry is null || !asset.Entry.StartsWith("originals/", StringComparison.Ordinal)
                || asset.Entry.Contains("..", StringComparison.Ordinal) || asset.Entry.Contains('\\')
                || string.IsNullOrWhiteSpace(asset.FileName) || asset.FileName.Contains('/') || asset.FileName.Contains('\\')
                || asset.Length < 0 || asset.Length > MaximumExpandedBytes - total || asset.Sha256 is not { Length: 64 })
                throw new InvalidDataException("원본 파일 정보가 올바르지 않습니다.");
            total += asset.Length;
        }
        if (m.Originals.Select(a => a.Entry).Distinct().Count() != m.Originals.Length
            || m.Originals.Select(a => a.FileName).Distinct().Count() != m.Originals.Length)
            throw new InvalidDataException("원본 파일 항목이 중복되었습니다.");
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] data, CancellationToken cancellationToken)
    {
        await using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await stream.WriteAsync(data, cancellationToken);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long declaredLength, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > declaredLength) throw new InvalidDataException("압축 파일의 실제 크기가 명시된 크기를 초과합니다.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != declaredLength) throw new InvalidDataException("압축 파일이 완전하지 않습니다.");
        return buffer.ToArray();
    }
}
