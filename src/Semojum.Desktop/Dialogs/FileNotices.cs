namespace Semojum.Desktop.Dialogs;

internal static class FileNotices
{
    internal static NoticeDialog UnsupportedFormat() => new("이 형식은 열 수 없습니다.", "지원 형식: PDF · HWP · HWPX · TXT · JPG · JPEG · PNG · SMJ", ["다시 선택", "취소"], error: true, width: 471);
    internal static NoticeDialog OversizedFile() => new("파일이 100 MB를 넘습니다.", "파일 크기를 줄이거나 나누어 다시 올려 주세요.", ["도움말", "취소"], error: true, width: 440);
    internal static NoticeDialog EngineUnavailable() => new("점역 엔진이 응답하지 않습니다.", "점자 패널만 잠깁니다. 텍스트 수정과 저장은 가능합니다.", ["다시 시도", "나중에"], error: true, width: 456);
    internal static NoticeDialog ReadOnlyDocument(bool accessDenied = false) => new(accessDenied ? "이 문서에 쓰기 권한이 없습니다." : "이 문서는 다른 창에서 열려 있습니다.", "이 창에서는 읽기 전용으로 엽니다.", ["읽기 전용으로 열기", "취소"]);
    internal static NoticeDialog InvalidRange(int start, int end, string detail) => new("고른 범위에 내용이 없습니다.", $"시작 {start}쪽, 끝 {end}쪽입니다. {detail}", ["범위 다시 지정", "취소"], width: 440);
    internal static NoticeDialog SaveFailed(string detail) => new("저장하지 못했습니다.", "고친 내용은 그대로 남아 있습니다.", ["다른 이름으로 저장", "다시 시도"], error: true, height: 206) { ToolTip = detail };
    internal static NoticeDialog PartialConversion(int total, int succeeded, int[] failed)
    {
        // BE 2026-10-27 POST /retry re-queues AI-failed pages only; canceled or credit-exhausted pages are reported back as "no pages".
        return new NoticeDialog($"{total}쪽 중 {failed.Length}쪽을 변환하지 못했습니다.", $"{string.Join(" · ", failed)}쪽입니다. 나머지 {succeeded}쪽은 사용할 수 있습니다.\n재변환 시 차감과 중복 차감 방지 여부는 확인되지 않았습니다. 마이페이지 사용량을 확인해 주세요.", ["실패한 쪽 다시 변환", "그대로 열기"], height: 236);
    }
    internal static NoticeDialog UnreadableCharacters(int count) => new($"원본에서 글을 읽지 못한 자리가 {count}군데 있습니다.", "■ 로 표시했습니다. 원본 이미지를 보고 직접 입력하십시오.", ["해당 위치로 이동", "확인"]);
    internal static NoticeDialog ConversionFailed() => new("변환하지 못했습니다.", "같은 파일로 다시 시도하거나 변환 설정을 바꾸십시오.", ["다시 시도", "변환 설정", "문의하기"], error: true, height: 206);
    internal static NoticeDialog Disconnected(int completed)
    {
        // 이어서 변환 re-attaches to the job status stream and fetches only the pages this window has not received.
        return new NoticeDialog("서버와 연결이 끊겼습니다.", $"수신한 {completed}쪽의 내용은 현재 문서에 남아 있습니다. Ctrl+S로 저장해 주세요.", ["이어서 변환", "닫기"]);
    }
    internal static NoticeDialog Offline(string detail) => new("서버에 연결하지 못했습니다.", "저장과 편집은 가능합니다. 변환과 게시판은 연결이 복구된 뒤에 사용할 수 있습니다.", ["다시 시도", "오프라인으로 계속"], error: true, width: 641) { ToolTip = detail };
    internal static NoticeDialog NoCredits() => new("크레딧이 모두 소모되어 변환을 멈췄습니다.", "구매 문의로 충전 방법을 확인한 뒤 상단 알림에서 이어서 처리할 수 있습니다.\n재개 시 차감·중복 차감 방지 여부는 확인되지 않았습니다. 마이페이지 사용량을 확인해 주세요.", ["구매 문의", "문의하기", "닫기"], width: 613, height: 226);
    // Server policy (LoginAttemptService.MAX_FAILURES = 5, LOCK_DURATION = 10 minutes) behind AUTH4001 remainingAttempts / AUTH4006 unlockAt.
    private const string LoginLockPolicy = "5회 실패하면 10분 동안 잠깁니다.";
    internal static NoticeDialog LoginRejected(Semojum.Core.Integration.SemojumApiException error)
    {
        var detail = "아이디와 비밀번호를 확인한 뒤 다시 입력해 주세요.";
        if (error.Details is { ValueKind: System.Text.Json.JsonValueKind.Object } data)
        {
            if (data.TryGetProperty("remainingAttempts", out var attempts)) detail = $"남은 횟수 {attempts}회입니다. {LoginLockPolicy}";
            if (data.TryGetProperty("unlockAt", out var unlock)) detail = $"다시 시도할 수 있는 시각: {unlock}";
        }
        return new(error.Message, detail, ["다시 입력", "비밀번호 문의"], error: true, width: 453);
    }
}
