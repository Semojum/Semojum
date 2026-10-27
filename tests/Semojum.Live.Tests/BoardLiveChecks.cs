using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core.Integration;

internal static class BoardLiveChecks
{
    private const string Title = "[QA4 검증] 게시판 등록·수정·삭제 테스트";
    private const string Body = "세모점 데스크톱 앱의 게시판 기능을 확인하기 위한 검증용 비밀글입니다. 실제 문의가 아니며, 첨부 내려받기와 글 수정 확인 후 삭제합니다.";
    private const string UpdatedTitle = Title + " (수정 확인)";
    private const string UpdatedBody = Body + "\n제목·본문 수정과 기존 첨부 유지 확인용 문장입니다.";
    private const string AttachmentName = "QA4-test-attachment.txt";
    private static readonly byte[] AttachmentBytes = Encoding.UTF8.GetBytes("Synthetic QA attachment. No personal or business information.");

    public static async Task RunAsync(string directory)
    {
        var output = Path.GetFullPath(directory); Directory.CreateDirectory(output);
        var statePath = Path.Combine(output, "board-state.json");
        var state = File.Exists(statePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(statePath))! : new State();
        void Persist() => File.WriteAllText(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        void Log(string value)
        {
            var line = $"{DateTimeOffset.Now:O} {value}";
            Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "board-checks.txt"), line + Environment.NewLine);
        }
        void Check(bool value, string name) { if (!value) throw new InvalidDataException(name); Log("PASS " + name); }
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        var api = new SemojumApiClient(http); var board = new BoardClient(api);
        bool IsOwnedTest(BoardPost p) => p.Mine && p.Secret && p.PostId == state.PostId
            && p.Title is Title or UpdatedTitle && p.Content is Body or UpdatedBody;
        async Task CleanupAsync()
        {
            if (state.PostId is not { } id || state.Deleted) return;
            try
            {
                var owned = await board.ReadAsync(id);
                if (!IsOwnedTest(owned)) throw new InvalidDataException("Recorded post no longer matches the private test; cleanup stopped");
                await board.DeleteAsync(id);
            }
            catch (SemojumApiException e) when (e.Status == HttpStatusCode.NotFound) { }
            bool gone = false;
            try { await board.ReadAsync(id); }
            catch (SemojumApiException e) when (e.Status == HttpStatusCode.NotFound) { gone = true; }
            Check(gone, "board test post detail returns 404 after deletion");
            var mine = await board.ListAsync(1, "TITLE_CONTENT", Title, true);
            Check(mine.Items.All(p => p.PostId != id), "board test post removed from my posts");
            state.Deleted = true; Persist();
        }
        try
        {
            var login = Environment.GetEnvironmentVariable("SEMOJUM_TEST_LOGIN") ?? throw new InvalidOperationException("Test login required");
            var password = Environment.GetEnvironmentVariable("SEMOJUM_TEST_PASSWORD") ?? throw new InvalidOperationException("Test password required");
            var account = await api.LoginAsync(login, password);
            password = ""; Environment.SetEnvironmentVariable("SEMOJUM_TEST_PASSWORD", null);
            Check(account.Role == "ROLE_USER", "board verification authenticates as ordinary user");
            if (state.Deleted) { Log("COMPLETE recorded board test already cleaned up; no new post created"); return; }
            if (state.PostId is null)
            {
                var existing = await board.ListAsync(1, "TITLE_CONTENT", Title, true);
                if (state.CreationAttempted)
                {
                    // A POST with a lost response must never be blindly repeated.
                    var candidates = existing.Items.Where(p => !state.BaselineIds.Contains(p.PostId) && p.Secret && p.Title == Title).ToArray();
                    if (candidates.Length != 1) throw new InvalidOperationException("Previous creation outcome is uncertain; no new post sent");
                    var recovered = await board.ReadAsync(candidates[0].PostId);
                    if (!recovered.Mine || recovered.Content != Body) throw new InvalidDataException("Recovery post does not match the approved test");
                    state.PostId = recovered.PostId; Persist();
                }
                else
                {
                    state.BaselineIds = existing.Items.Select(p => p.PostId).ToArray();
                    state.CreationAttempted = true; Persist();
                    var created = await board.SaveAsync(null, Title, Body, true, [], [new(AttachmentName, AttachmentBytes)]);
                    state.PostId = created.Id; Persist();
                    Log("CREATED private test post " + created.Id);
                }
            }
            var post = await board.ReadAsync(state.PostId.Value);
            Check(IsOwnedTest(post) && !post.Hidden, "private board post round-trip and owner permissions");
            var list = await board.ListAsync(1, "TITLE_CONTENT", Title, true);
            Check(list.Items.Any(p => p.PostId == post.PostId && p.Secret), "private board post appears in my posts search");
            Check(post.Attachments is [{ FileName: AttachmentName }] && post.Attachments[0].Size == AttachmentBytes.Length,
                "board attachment name and length preserved");
            var attachment = post.Attachments[0];
            Check(Uri.TryCreate(attachment.Url, UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0,
                "board attachment download uses HTTPS");
            using var download = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
            using var response = await download.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Check(bytes.SequenceEqual(AttachmentBytes), "board attachment download bytes match synthetic upload");
            await File.WriteAllBytesAsync(Path.Combine(output, AttachmentName), bytes);
            var saved = await board.SaveAsync(post.PostId, UpdatedTitle, UpdatedBody, true, [attachment.AttachmentId], []);
            Check(saved.Id == post.PostId && saved.Post is { Mine: true, Secret: true, Title: UpdatedTitle, Content: UpdatedBody },
                "board update response preserves owner and privacy");
            var updated = await board.ReadAsync(post.PostId);
            Check(updated.Title == UpdatedTitle && updated.Content == UpdatedBody && updated.Secret,
                "board edited title and body survive reopening");
            Check(updated.Attachments.Length == 1 && updated.Attachments[0].AttachmentId == attachment.AttachmentId,
                "board edit keeps the original attachment");
            state.ContentVerified = true; Persist();
            await CleanupAsync();
            Log("COMPLETE private board create/read/attachment/update/delete; no operator answer or inquiry sent");
        }
        catch (Exception e)
        {
            Log("FAIL " + e.GetType().Name + (e is SemojumApiException apiError ? " " + apiError.Code : ""));
            Environment.ExitCode = 1;
        }
        finally
        {
            if (api.Account is not null)
            {
                try { await CleanupAsync(); }
                catch (Exception e) { Log("FAIL cleanup " + e.GetType().Name); Environment.ExitCode = 1; }
                try { await api.LogoutAsync(); Log("PASS board verification session logout"); }
                catch (Exception e) { Log("WARN logout " + e.GetType().Name); }
            }
        }
    }

    private sealed class State
    {
        public bool CreationAttempted { get; set; }
        public long[] BaselineIds { get; set; } = [];
        public long? PostId { get; set; }
        public bool ContentVerified { get; set; }
        public bool Deleted { get; set; }
    }
}
