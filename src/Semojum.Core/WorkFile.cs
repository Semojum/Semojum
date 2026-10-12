using System.Text.Json;

namespace Semojum.Core;

public sealed record BlockSnapshot(int Id, int Page, string Heading, string Source, string Braille, string Memo, bool HasManualBraille);
public sealed record WorkSnapshot(string Format, int Version, PageLayout Layout, BlockSnapshot[] Blocks);

public static class WorkFile
{
    public const string Format = "semojum-mockup";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static WorkSnapshot Snapshot(DocumentSession session) => new(Format, 1, session.Layout,
        session.Blocks.Select(b => new BlockSnapshot(b.Id, b.Page, b.Heading, b.Source, b.Braille, b.Memo, b.HasManualBraille)).ToArray());

    public static async Task SaveAsync(string path, WorkSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static async Task<DocumentSession> LoadAsync(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("목업 작업 파일은 20 MB까지 열 수 있습니다.");
        var snapshot = JsonSerializer.Deserialize<WorkSnapshot>(await File.ReadAllTextAsync(path), JsonOptions)
            ?? throw new InvalidDataException("작업 파일을 읽을 수 없습니다.");
        if (snapshot.Format != Format || snapshot.Version != 1 || snapshot.Layout is null || snapshot.Blocks is not { Length: > 0 })
            throw new InvalidDataException("세모점 목업에서 저장한 .mock.smi 파일만 열 수 있습니다.");
        snapshot.Layout.Validate();
        if (snapshot.Blocks.Any(b => b is null || b.Source is null || b.Braille is null || b.Memo is null)
            || snapshot.Blocks.Select(b => b.Id).Distinct().Count() != snapshot.Blocks.Length)
            throw new InvalidDataException("문서 블록 데이터가 올바르지 않습니다.");
        var session = new DocumentSession { Title = Path.GetFileName(path), Layout = snapshot.Layout };
        foreach (var b in snapshot.Blocks) session.Add(new EditorBlock
        {
            Id = b.Id, Page = b.Page, Heading = b.Heading, Source = b.Source, Braille = b.Braille,
            Memo = b.Memo, HasManualBraille = b.HasManualBraille, State = b.HasManualBraille ? "직접 수정" : "샘플 점역"
        });
        session.IsDirty = false;
        return session;
    }
}
