# 세모점 데스크톱 클라이언트 잔여 작업 인계

작성일: 2026-10-20. 다른 세션에서 구현을 이어가기 위한 문서다. 최초 작성 세션은 코드 대조와 인계 문서 작성만 수행했고, 이후 세션의 진행은 0절에 기록한다.

## 0. 2026-10-20 진행 기록 (이 문서 작성 이후 세션)

이 절은 아래 원래 인계 내용 위에 덧붙인 진행 상태다. 원래 내용은 근거로 남겨 둔다.

**확인:** 로컬 `main` 8e63161, 기존 변경(`docs/backend-save-review.md`, `output/`) 보존. BE `dev` 원격은 여전히 `5d8dda0a`라 B-01~B-10 계약 변경이 하나도 배포·병합되지 않았다(`clientId`, `resolved`, `even`, `numberRanges`, `/retry`, 암호 필드, 신뢰도 필드 모두 없음). 따라서 C-05~C-10 서버 연결은 시작할 수 없고 준비 작업만 했다.

**구현 (앱 코드만 변경, BE 미변경):**

- C-01 `JobClient.PlanSave`/`ApplySaved`, `DocumentSession.ServerPages`, `.smj` v3 `serverPages`. 서버 id가 있는 모든 블록(차단 블록 포함)과 메모를 쪽 스냅샷으로 전송, 실패/대기 표시 블록은 제외, 점자 패널은 서버 보유 기록이 있는 id만 전송(기록 없으면 `null`=변경 없음), 모두 지운 쪽은 빈 배열 전송 후 ‘빈 쪽으로 남음(쪽 삭제 미지원)’ 안내, 새 블록·중복 id는 전송 전 중단, 성공 판정은 전송 계획 쪽 수 기준. 메모 `createdAt`은 서버 `LocalDateTime`에 맞춰 오프셋 없이 전송.
- C-02 `JobStatus`에 처리 종료·성공·실패·변환 중·남은 수를 분리하고 `ConversionProgress`를 그 값으로 구성. ETA는 처리 종료 수 기준. `RUNNING` 표시 추가.
- C-03 `quality_report`를 `QualityIssue`로 읽어 `DocumentSession.QualityReports`와 `.smj` v3 `qualityReports`에 보존. 쪽별 변환 상태 대화상자에 쪽별 검토 항목(구분·유형·내용·블록·원문 수정 여부)과 ‘항목의 블록으로 이동’ 추가. 연결 불가 id는 쪽 수준으로 표시. OCR 신뢰도는 표시하지 않음.
- C-04 `ServerContract`: ‘짝수 페이지’는 `pageNumberLine` 키를 생략(서버 값 유지)하고 저장 결과·업로드 확인·판면 설정 상태에 서버 미보존 항목(짝수 면·번호 범위·꼬리말 출력 면·꼬리말 범위·짝수 면 보정·메모 해결 상태·로컬 점자)을 안내. 서버 양식 저장은 미지원 값이면 거부(값을 바꿔 저장하지 않음).
- 검사: `tests/Semojum.Core.Tests/SyncChecks.cs` 5건 추가, 기존 `JobSave` 검사 갱신. WPF smoke의 쪽 상태 캡처에 품질 보고서 fixture 추가.

**검증:** [검증 기록](verification.md) 최신 항목 참조. Windows 실행·WPF 캡처는 저장소에 push 후 기존 워크플로로 다시 받아야 한다(이 세션은 GitHub push 권한이 없었다).

**사용자 확인이 필요한 사항:**

1. 블록을 모두 지운 쪽을 현재 계약대로 ‘빈 쪽’(빈 배열 전송)으로 서버에 반영하는 방식이 맞는지. 대안은 그 쪽을 요청에서 빼서 서버에 옛 블록을 남기는 것(다시 열면 지운 블록이 되살아남).
2. 점자 패널: 서버가 같은 id의 점자 항목을 갖지 않은 블록(V3.1 새 작업 전부)은 서버에 보내지 않고 `.smj`에만 보존한다. B-01 전까지 이 방식(원문·메모만 서버 저장)으로 갈지, 아니면 서버 저장 자체를 중단할지.
3. `.smj` 형식 버전을 3으로 올렸다(v2 앱은 v3 파일을 열지 않음). 이전 v1→v2 결정과 같은 이유인데, 유지할지.
4. 서버 미보존 항목 안내를 저장 성공 직후 대화상자(문서별로 내용이 바뀔 때 1회)로 띄운다. 상태 표시줄만으로 충분하면 줄일 수 있다.

## 1. 저장소와 기준

- 저장소: [Semojum/Semojum](https://github.com/Semojum/Semojum). 로컬 경로: `/Users/adamatia123/Documents/ChatGPT/세모점`.
- 확인한 로컬 브랜치: `main`, 커밋 `8e63161155b40c7e93de183886899b06fcec5faa`. [PR #2](https://github.com/Semojum/Semojum/pull/2)의 데스크톱 편집·조판 변경이 반영된 상태다. 다음 세션 시작 시 원격 main과 작업 중 변경을 다시 확인한다.
- 기술: C#/.NET 10, WPF, AvalonEdit. 로컬 점역 엔진은 `Semojum/braille` **v3.4.24**, 커밋 `976b2048d1a1ad0b31a869001f80f9fa260b93af`에 고정되어 있다.
- 기획 우선순위: 사용자 확정 사항 → [Service](https://app.notion.com/p/3a643813bc2e801aa2b9eaf48dde8f2e)의 [최신 세부 기능](https://app.notion.com/p/3ef43813bc2e802f9ea2c8a7858b9083) → 나머지 기획·Figma. 구현에 들어갈 때 원문 변경 여부를 확인한다.
- Figma 참고: [Semojum-UI 복사본](https://www.figma.com/design/2s2voe2zE4M6xaKwYZRB6J/Semojum-UI--%EB%B3%B5%EC%82%AC-?node-id=1-3). 모든 상태의 시각 검증이 완료된 것은 아니다.
- 상세 근거: [구현 상태](implementation-status.md), [서비스 우선순위](service-priority.md), [BE 수정 목록](backend-save-review.md), [엔진 계약](engine-contract.md), [검증 기록](verification.md).

인계 작성 시 기존 작업 파일 변경은 `docs/backend-save-review.md`와 `output/pdf/세모점_백엔드_수정_목록.pdf`다. 이 인계 문서도 로컬 파일이므로 별도 체크아웃/다른 PC에서 이어갈 때 함께 전달해야 한다. 기존 변경을 초기화하거나 덮어쓰지 않는다.

## 2. 이미 확정된 조건

- **자동 저장 없음.** Ctrl+S·명시적 최종 저장만 로컬 `.smj`와 서버 저장을 요청한다. 로컬을 먼저 보존하고 서버 실패는 구분한다.
- `.smj`는 버전 있는 ZIP. 현재 v2, 기존 v1 읽기를 지원한다. 원본·원문·점자·메모·옵션·커서 복원과 수동 점자 보존을 유지한다.
- 선택한 점자 면의 TXT에는 조판된 **점자 그대로** 저장한다.
- 머리말을 면 중간에 삽입하면 **현재 면 맨 위부터** 변경한다.
- 판면의 칸·줄 설정은 작업 전체 한 세트다. `editScope`를 면별 판면 크기로 확대 해석하지 않는다.
- **BE 저장소는 수정하지 않는다.** 계약 차이는 제안 문서로 남긴다. 첫 점자·신규 블록 저장을 임의 UUID/본문 비교로 우회하지 않는다.
- **점역 엔진 규칙 확장은 후속.** 영어 문서·한영 혼용·연속 공백 정책을 앱 자체 규칙으로 추측하지 않는다.
- 관리자 웹·서버 폴더·휴지통은 후속 범위다. 로컬 작업 폴더와 혼동하지 않는다.
- 6점키는 사용자 지정이 구현되어 있다. 미확정 1·2·3단 배열은 임의로 만들지 않는다.
- 문의 주소는 `contact-semojum@gmail.com`. 운영 기관명은 미정이다.

## 3. 이미 구현된 기능

다중 탭, 로컬 작업 폴더, `.smj` 저장·복원, 원문/점자 편집, 병합·분할·정렬, Undo/Redo, 원문 연속 편집, BRF 기반 모눈 직접 편집, 독립 창 분리·복귀, 수동 점자 보호, 표·수식·기호, 메모·답글·해결, 현재 면부터 머리말 적용, 번호·꼬리말 범위와 정렬, 범위 내보내기, 미리보기, RAW 인쇄 경로, 계정·양식·공지·게시판·업데이트 경로는 존재한다.

이를 전부 미구현으로 간주해 다시 만들지 않는다. 아래 목록은 실제 코드에서 확인한 남은 구현과 별도 검증이다. 특히 ‘클라이언트 코드가 있음’, ‘가짜 HTTP/CI 통과’, ‘운영 서버·사용자 장비 검증 완료’를 구분한다.

## 4. 백엔드 변경 없이 먼저 진행할 작업

### C-01. 저장 요청에서 차단 블록·빈 쪽 누락 방지 — 최우선

**현재:** `JobClient.SaveAsync`가 `!IsBlocked` 블록만 모아 쪽을 구성한다. BE는 전송된 패널 배열에서 빠진 기존 블록을 삭제한다. 정상·차단 블록이 섞인 쪽에서 보존해야 할 블록과 메모가 누락될 수 있다. `MainWindow.SaveAsync`의 예상 저장 쪽 수도 같은 필터를 사용한다. 마지막 블록을 지워 비게 된 쪽은 `GroupBy` 결과에 나타나지 않는다.

**할 일:**

- 저장 요청을 만들기 전에 실제 서버 블록과 로컬 실패/대기 표시용 블록을 구분한다. 실패 쪽 표시용 블록은 `ServerId`가 없으므로 전부 전송하는 방식도 잘못이다.
- 보존해야 할 서버 블록·메모를 누락하지 않는 전체 스냅샷 구성을 만든다. 현재 계약으로 안전하게 표현할 수 없으면 해당 서버 저장을 명확히 중단하고 로컬 파일을 보존한다.
- ‘편집 제외’와 ‘삭제’를 분리한다. 빈 쪽·삭제된 쪽·전체 차단 쪽의 의미를 BE 문서 B-02/B-03과 맞춘다. 쪽 삭제를 아직 지원하지 않는 계약에서 처리한 것처럼 표시하지 않는다.
- 저장 성공 판정의 예상 쪽 수는 실제 전송 계획에서 계산한다. 데이터 일부를 누락해 놓고 0쪽 또는 소수 쪽 저장을 전체 성공으로 표시하지 않는다.

**수정 위치:** [JobClient.cs](../src/Semojum.Core/Integration/JobClient.cs)의 `SaveAsync`, [MainWindow.xaml.cs](../src/Semojum.Desktop/MainWindow.xaml.cs)의 `SaveAsync`, [IntegrationChecks.cs](../tests/Semojum.Core.Tests/IntegrationChecks.cs)의 `JobSave`.

**완료 조건:** 정상+차단 혼합, 전체 차단, 서버 ID 없는 실패 표시 블록, 마지막 블록 삭제, 동일 쪽 메모의 요청 본문을 검사한다. 위험한 누락 저장이 전송되거나 전체 성공으로 표시되지 않는다. BE의 최종 삭제 계약 연결은 C-06에서 한다.

### C-02. 변환 진행률의 성공·실패 중복 집계 수정

**현재:** 검토한 BE `5d8dda0a`의 `JobService.getJobStatus`에서 `completedPages`는 `COMPLETED/NEEDS_REVIEW/BLOCKED`를 모두 포함하는 처리 종료 수다. 앱은 이를 성공 수로 넘기고 `ConversionProgress.Finished`에서 실패 수를 다시 더한다. 예상 잔여 시간에서도 실패 수를 중복 차감한다.

예: 전체 10쪽, 성공 2쪽, 실패 1쪽이면 처리 완료는 3쪽인데 현재 계산은 ‘4쪽 처리·성공 3·실패 1’이 될 수 있다. 코드에서 도출한 사례이며 운영 재현은 아니다.

**할 일:** 페이지 상태와 서버 필드의 의미에 맞춰 처리 종료 수·성공 수·실패 수·남은 수를 분리한다. `NEEDS_REVIEW`는 결과가 있는 검수 대상임을 유지한다. `RUNNING` 등 서버 상태의 표시도 맞춘다. 취소·전체 실패·SSE 재연결 시 상태 일관성을 확인한다.

**수정 위치:** [MainWindow.Jobs.cs](../src/Semojum.Desktop/MainWindow.Jobs.cs)의 `MonitorJobAsync`, [DocumentSession.cs](../src/Semojum.Core/DocumentSession.cs)의 `ConversionProgress`, [PageStatusDialog.cs](../src/Semojum.Desktop/Dialogs/PageStatusDialog.cs).

**완료 조건:** 위 10쪽 사례가 ‘3쪽 처리·성공 2·확인 필요 1’로 표시된다. 남은 수와 ETA가 실패 때문에 추가로 줄지 않고, 종료 여부는 실제 서버 상태와 일치한다.

### C-03. 이미 제공되는 품질 보고서 표시

**현재:** 서버 페이지의 `result.quality_report.critical_errors/review_flags`에는 `type/element_id/message`가 있다. `JobClient.ReadPage`는 이를 읽지 않는다. `is_blocked`와 점역 엔진의 로컬 경고는 일부 표시하지만 서버 검토 사유가 빠져 있다.

**할 일:** 품질 보고서를 모델에 보존하고 쪽/블록의 검토 사유 목록과 해당 블록 이동을 연결한다. 존재하지 않거나 삭제된 `element_id`의 경고는 쪽 수준에서라도 보존한다. 반복 조회 시 중복되지 않게 하고 원문 수정 후 AI 원본 경고와 현재 편집을 구분한다. 오프라인 재열람에 필요한 값은 `.smj`에 호환성 있게 보존한다.

**수정 위치:** [JobClient.cs](../src/Semojum.Core/Integration/JobClient.cs)의 `RemotePage/ReadPage`, [BrailleTranslation.cs](../src/Semojum.Core/BrailleTranslation.cs)의 메타데이터 모델, [SmjFile.cs](../src/Semojum.Core/SmjFile.cs), [MainWindow.Jobs.cs](../src/Semojum.Desktop/MainWindow.Jobs.cs), [PageStatusDialog.cs](../src/Semojum.Desktop/Dialogs/PageStatusDialog.cs).

**완료 조건:** 오류/검토 항목 있음·없음·알 수 없는 유형·블록 연결 불가 사례를 표시하고 다시 열어도 보존한다. **OCR 신뢰도 수치와 문자별 위치는 서버/AI 확장 전까지 임의로 생성하지 않는다.** BE B-10 중 기존 필드 활용만 먼저 진행한다.

### C-04. 서버가 보존하지 못하는 옵션을 조용히 변환하지 않도록 수정

**현재:** `LayoutBody`가 ‘짝수 페이지’를 `none`으로 보낸다. 번호 범위·독립 꼬리말 출력 정책·범위·마지막 짝수 면 보정은 서버 요청/응답에 없다. 양식도 같은 변환 함수를 사용한다. 저장 성공 뒤 안내는 꼬리말 범위에만 일부 제공된다. 메모 해결 상태 역시 로컬에만 있다.

**할 일:** 로컬 기능은 유지하면서 서버에 그대로 저장 가능한 값과 로컬에만 보존되는 값을 명확히 알린다. 지원되지 않는 설정으로 서버 양식을 저장할 때 다른 값으로 조용히 바꾸지 않는다. 기존 서버에 미지원 필드를 보내고 저장된 것으로 표시하는 방식은 금지한다. 지원 여부가 불명확하면 검증된 계약 기준으로 처리한다.

**수정 위치:** [JobClient.cs](../src/Semojum.Core/Integration/JobClient.cs)의 `LayoutBody/ReadLayout`, [SettingsClient.cs](../src/Semojum.Core/Integration/SettingsClient.cs), [SettingsDialogs.cs](../src/Semojum.Desktop/Dialogs/SettingsDialogs.cs), [MainWindow.xaml.cs](../src/Semojum.Desktop/MainWindow.xaml.cs)의 저장 결과 표시.

**완료 조건:** ‘짝수 면’이 서버에서 ‘번호 없음’으로 바뀌었는데 저장 완료라고 알리지 않는다. 서버에서 복원되지 않는 값과 로컬 `.smj` 저장 결과를 구분한다. 실제 옵션 왕복 지원은 C-08이다.

## 5. 백엔드 계약이 확정되면 연결할 작업

BE 제안 문서의 필드와 엔드포인트는 아직 제공되는 API가 아니다. 다음 세션에서 실제 계약과 배포 여부를 먼저 확인한다. 어댑터·가짜 응답 테스트는 준비할 수 있지만 서버 동기화 완료로 표시하지 않는다.

| 항목 | 클라이언트에서 할 일 | 선행 조건 / 완료 조건 |
|---|---|---|
| **C-05. 신규 블록·첫 점자 ID 동기화** | 재시도에도 같은 블록을 식별하는 client ID를 저장 파일에 보존. 성공한 쪽의 서버 ID를 블록·메모에 반영. 삭제 저장 후 Undo의 복원 처리. 신규 ID 가드 제거는 계약 지원 시에만 수행 | BE B-01. 동일 문장 2개, 분할·병합·재정렬, 응답 유실, 재실행 후 재전송에서 ID 중복/오연결 없음 |
| **C-06. 쪽별 서버 저장 결과·재시도** | `JobSaveResult` 확장, 성공/실패 쪽·원인·재시도 가능 여부 표시. 전체 스냅샷/부분 저장/삭제 요청 연결. 성공한 데이터만 확정하고 저장 중의 새 편집은 유지 | BE B-02/B-03/B-07. 부분 실패 재시도로 다른 쪽 삭제·쪽 수 감소가 없고, 설정만 성공한 상태도 구분 |
| **C-07. 메모 서버 왕복** | `resolved` 전송·복원, 서버 memo ID 매핑, 생성 시각 유지. 신규/삭제/복원 블록의 메모 연결. 위치를 잃은 메모 보존 | BE B-05. 반복 저장·다시 열기에서 메모 ID·해결 상태·연결을 유지 |
| **C-08. 조판 옵션 서버 왕복** | `even`, 번호 범위, 독립 꼬리말 정책, 범위별 내용/정렬, `PadToEven`의 요청·응답 연결. 작업 설정·계정 기본값·양식 적용 경로 점검 | BE B-06. `.smj` 없이 서버에서 열어도 동일 설정 복원. 구 작업/양식 기본값과 생략/빈 범위 의미 유지 |
| **C-09. 실패 쪽 AI 재시도 UI** | 쪽 상태 화면에서 대상 선택·비용 안내·재시도·진행/취소·결과 반영. 기존 편집 보호, 중복 클릭 방지 | BE B-08의 API·과금·결과 교체 정책. 전체 파일 유료 재업로드로 우회하지 않음 |
| **C-10. PDF 암호·HWP 옵션·OCR 확장** | 암호 요청/오류 재입력, 실제 지원하는 HWP 옵션 선택, 신뢰도·미지원 문자 위치 표시 | BE B-09/B-10 및 AI 응답. 현재 필드와 정책이 없는 부분을 임의로 가정하지 않음 |

공통 수정 시작점은 `src/Semojum.Core/Integration/JobClient.cs`, `DocumentSession.cs`, `SmjFile.cs`, `src/Semojum.Desktop/MainWindow.Jobs.cs`, `MainWindow.xaml.cs`다. 메모는 `Dialogs/MemoThreadDialog.cs`, 설정은 `Integration/SettingsClient.cs`와 `Dialogs/SettingsDialogs.cs`를 함께 본다.

C-05/C-06에서는 서버 ID 반영이 저장 중 생성된 다른 문서/블록에 적용되지 않도록 원래 문서와 스냅샷을 기준으로 처리한다. 명시적 저장 중 서버 응답까지 받은 뒤 저장 파일의 동기화 메타데이터를 어떻게 확정할지 설계하고, 오래된 로컬 스냅샷을 다시 열어도 안정적인 client ID로 중복 생성을 막는다. 자동 저장을 도입하지 않는다. 원본 쪽을 넘는 블록 이동·구조 변경은 서버 계약 없이 제한을 풀지 않는다.

## 6. 구현과 구분해 남겨 둘 실제 환경 검증

| 검증 | 확인할 시나리오 | 주요 코드 |
|---|---|---|
| 한국어 IME·6점키 | 조합 중 선택/삭제/Undo, 포커스 이동, 키 반복·동시 입력·키 놓침, 사용자 지정 6키 | `ContinuousSourceEditor.cs`, `BraillePane.xaml.cs`, `BrailleGridView.Editing.cs`, `SixDotInput.cs` |
| 접근성 | 키보드만으로 이동·편집·대화상자 종료, 스크린 리더의 모눈/선택/오류 안내 | `Controls/`, `MainWindow.xaml`, `Dialogs/` |
| 다중 모니터·DPI | 메인/분리 창 100/150/200% 이동, 모눈 클릭 좌표·커서·선택·PDF 비율 | `MainWindow.Panels.cs`, `BrailleGridView.cs`, `OriginalPreview.xaml.cs` |
| 대용량 실원고 | 실제 PDF/HWP/이미지, 대량 블록 편집·조판·메모·탭 전환·취소 시 지연과 메모리 | `MainWindow.Jobs.cs`, `MainWindow.Composition.cs`, `LocalBrailleEngine.cs` |
| 저장·업데이트·인쇄 | 네트워크 공유 폴더/다중 PC 잠금, 권한 부족/저장 중 종료 복구, 포터블 업데이트 전체 흐름, 실제 점자 프린터 | `WorkFileLease.cs`, `SmjFile.cs`, `PortableUpdate.cs`, `AppUpdates.cs`, `RawBraillePrinter.cs` |
| 서버 E2E | 로그인/만료/잠금, 업로드·SSE·취소, 서버 저장→재열람, 양식·게시판 첨부·사용량 | `Integration/`, `MainWindow.Jobs.cs`, `Dialogs/` |

이 항목들은 기능이 전부 없다는 뜻이 아니다. 실제 장비·테스트 계정·배포 환경에서 검증되지 않았다는 뜻이다. 결함이 재현되면 클라이언트를 수정한다. 유료 AI 변환이나 게시판 게시 등 외부 변경을 테스트할 때는 사용자가 허용한 대상과 비용 범위 안에서 진행한다.

Figma의 오류·로딩·빈 상태·읽기 전용·분리 창·설정 대화상자도 실제 Windows 캡처로 대조할 필요가 있다. 이미 반영된 화면을 전면 재설계하는 작업으로 확대하지 않는다. 운영 기관명과 미확정 6점키 프리셋은 값이 확정된 뒤 반영한다.

## 7. 검증 기준과 실행 방법

기존 최종 전달본의 근거는 [Windows 실행 39276049622](https://github.com/Semojum/Semojum/actions/runs/39276049622), 소스 커밋 `4ea411718aeb0f9f1b937c624ef670651515d037`이다. 로컬 전달 기록은 `artifacts/delivery/desktop-service-completion/검증.md`에 있다.

- Windows 핵심 검사 **55/55**, 실제 Python·패키징 엔진 포함 **57/57**.
- Python 조판 검사 **5개**, 그 안의 난수 BRF/좌표 비교 **160개**.
- WPF 검사 **73개**, 정상 종료 코드 **0**.
- 포터블 ZIP: `artifacts/delivery/desktop-service-completion/Semojum-win-x64.zip`.

이는 **이전 실행 기록**이다. 이번 인계 작성에서 재실행한 결과가 아니다. `docs/verification.md`에는 이전 실행의 WPF 72개도 남아 있으므로 최신 결과를 인용할 때 실행 ID·소스 커밋과 함께 구분한다. 다음 코드 변경의 테스트 수는 새 실행 결과로 갱신한다.

기본 검사:

```powershell
dotnet build Semojum.sln -c Release
dotnet run --project tests/Semojum.Core.Tests -c Release --no-build
```

실제 엔진·배포물 검증은 Windows에서 기존 [워크플로](../.github/workflows/windows-mockup.yml)를 따른다:

```powershell
./scripts/publish.ps1
$env:SEMOJUM_ENGINE_PYTHON = Join-Path $PWD 'artifacts/engine-build/venv/Scripts/python.exe'
$env:SEMOJUM_ENGINE_EXECUTABLE = Join-Path $PWD 'artifacts/Semojum-win-x64/engine/semojum-engine.exe'
& $env:SEMOJUM_ENGINE_PYTHON tests/engine/test_editor_composition.py
dotnet run --project tests/Semojum.Core.Tests -c Release --no-build
```

WPF smoke는 `.github/workflows/windows-mockup.yml`처럼 `Semojum.Mockup.exe --smoke-test <출력 경로>`로 실행하고 프로세스 종료 코드·`checks.txt`·스크린샷까지 확인한다. macOS에서는 WPF 실행을 검증했다고 하지 않는다. 엔진 환경 변수가 없어서 SKIP된 검사를 실제 엔진 통과로 세지 않는다.

새 테스트는 `tests/Semojum.Core.Tests/IntegrationChecks.cs`, `RemainingChecks.cs`, `src/Semojum.Desktop/Services/WindowsSmoke.cs`의 기존 구조를 활용한다. 요청 본문·응답 파싱·중복 집계·보존/삭제 같은 실제 실패 사례를 검증한다. API 테스트 더블만 통과한 결과를 운영 E2E 성공으로 보고하지 않는다.

## 8. 다음 세션의 진행 순서

1. 현재 브랜치와 기존 변경을 확인하고 이 문서·Service 기획·관련 코드의 최신 상태를 대조한다. 이미 결정된 조건을 다시 묻지 않는다.
2. C-01 저장 누락 방어 → C-02 진행률 → C-03 품질 보고서 → C-04 미지원 옵션 안내를 구현하고 관련 검사로 확인한다.
3. BE 계약이 실제 확정/배포된 항목만 C-05~C-10의 서버 연결을 완료한다. 미확정 항목은 가능한 준비 작업과 필요한 계약을 기록한다.
4. 변경한 화면의 Windows 실행·캡처와 핵심 회귀 검사를 완료한다. 실제 장비/계정 검증이 남으면 구체적으로 남긴다.
5. 구현 상태·검증 문서를 실제 결과에 맞춰 갱신한다. 기획 해석이 필요한 질문은 근거·선택지와 함께 모아서 사용자에게 전달하고, 독립 작업은 계속한다.
