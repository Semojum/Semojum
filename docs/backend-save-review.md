> 2026-10-27: 이 문서는 이전 검토 기록입니다. 현재 구현·검증 및 남은 제약은 [QA 4차 결과](qa4-2026-10-27.md)를 우선 확인하세요.

# 데스크톱 편집기 연동을 위한 백엔드 수정 목록

2026-10-20 확인. [Semojum/BE `dev`, `5d8dda0a`](https://github.com/Semojum/BE/tree/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1)의 실제 코드와 [앱 `main`, `8e631611`](https://github.com/Semojum/Semojum/tree/8e63161155b40c7e93de183886899b06fcec5faa)을 대조했다. 확인 시점의 원격 BE `dev`와 검토용 체크아웃의 커밋이 같음을 확인했다.

**정적 코드 검토 결과와 수정 제안이다. 운영 서버 재현·배포 확인이나 백엔드 테스트 실행 결과는 아니다. 사용자 결정에 따라 BE 코드는 변경하지 않았다.** 서비스 우선순위와 합의된 범위는 [서비스 기획 적용 기록](service-priority.md)을 따른다. 아래 새 필드명·API 경로는 제안이며 현재 제공되는 계약으로 취급하면 안 된다.

## 우선순위

P0는 서버 저장을 연결하기 전에 해결할 정합성·권한 문제, P1은 편집 결과 보존과 오류 처리를 완성할 항목, P2는 추가 서버 기능이다. 이번 기술 검토의 우선순위이며 기획서의 기능 등급을 바꾸는 것은 아니다.

| 번호 | 우선순위 | 수정 대상 | 현재 영향 | 담당 범위 |
|---|---|---|---|---|
| B-01 | P0 | 첫 점자·신규 블록의 공통 ID, 삭제 후 복원 | 첫 점자 저장 실패, 신규 원문·점자 연결 불가 | BE + 앱 |
| B-02 | P0 | 부분 실패 시 쪽 수·쪽 삭제 처리 | 기존 쪽이 있는데 전체 쪽 수가 줄어들 수 있음 | BE + 앱 계약 |
| B-03 | P0 | 누락을 삭제로 처리하는 규칙과 차단 블록 보존 | 정상 블록 저장 중 차단 블록·메모가 빠질 수 있음 | 앱 수정 필수 + BE 방어 |
| B-04 | P0 | 작업 상태 조회의 소유자 검사 | 로그인 여부만 검사하고 작업 소유자를 검사하지 않음 | BE |
| B-05 | P1 | 메모 ID·해결 상태·연결 위치 | 저장마다 ID 재발급, 해결 상태 유실 | BE + 앱 |
| B-06 | P1 | 조판 옵션의 저장·조회·양식 왕복 | 짝수 면・번호 범위・꼬리말 정책/범위・양면 보정 유실 | BE + 앱 |
| B-07 | P1 | 저장 요청 검증·쪽별 실패 사유 | 어떤 데이터 때문에 실패했는지 앱에서 구분 불가 | BE + 앱 |
| B-08 | P2 | 실패한 쪽의 사용자 AI 재시도 | 워커 내부 자동 재시도만 있고 사용자 재시도 경로 없음 | BE + 앱, 과금 정책 확정 |
| B-09 | P2 | 암호 PDF·HWP 추출 옵션 전달 | 업로드 계약에 입력 필드 없음 | BE + 앱, 옵션 정의 |
| B-10 | P2 | OCR 신뢰도·미지원 문자 안내 | 저장된 신뢰도가 조회 응답에 없고 앱의 품질 보고서 연결도 필요 | BE + 앱 + AI 응답 확인 |

## B-01. 첫 점자·신규 블록 ID와 삭제 후 복원

**근거:** [PageContentSaver.java 105–187행](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/result/service/PageContentSaver.java#L105), [SaveResult](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/dto/JobResponseDto.java#L120).

- 기존 텍스트 ID로 첫 점자를 보내면 같은 ID의 `BrailleElement`가 없어 `ELEMENT_NOT_FOUND`가 발생한다.
- 두 패널에 `id: null`을 보내면 각각 다른 UUID를 발급한다. 저장 응답에 발급 ID 매핑도 없다. 앱과 조회 직렬화는 원문·점자를 같은 ID로 연결하므로 배열 순서나 본문 비교로 복구하면 안 된다.
- 기존 블록 검색은 삭제되지 않은 행만 대상으로 한다. 삭제를 서버에 저장한 다음 Undo로 같은 ID를 복원하면 현재 경로는 이를 찾지 못한다.

**수정 제안:** 두 패널에 공통으로 쓸 논리 블록 ID를 유지한다. `SaveElement`에 `clientId`를 추가하고, 같은 쪽·같은 `clientId`의 원문과 점자에 공통 `id`를 발급한다. `(job, page, clientId)` 중복 방지와 재시도 시 기존 매핑 반환이 필요하다. 기존 텍스트 ID의 첫 점자는 같은 ID로 생성한다. 다른 작업의 ID는 거부한다. 삭제된 ID의 복원은 소속과 삭제 상태를 검증하는 명시적 동작으로 제공한다.

요청·응답의 최소 변경 예시다. `clientId`, `blockIds`는 **새로 제안하는 필드**다.

```json
{
  "pages": [{
    "pageNo": 1,
    "text": [{"id": null, "clientId": "local-block-1", "contents": ["가"]}],
    "braille": [{"id": null, "clientId": "local-block-1", "contents": ["⠫"]}]
  }]
}
```

```json
{
  "savedPages": 1,
  "failedPages": [],
  "blockIds": [{"pageNo": 1, "clientId": "local-block-1", "id": "shared-server-id"}]
}
```

**앱 변경:** 신규 `ServerId`가 없는 블록의 서버 전송을 막는 검사를 새 계약 지원 여부에 맞게 바꾸고, 성공한 쪽의 ID 매핑을 문서와 메모에 반영한다. 기존 두 패널 ID가 다른 데이터는 마이그레이션 또는 명시적 대응표가 필요하다. 본문이 같다는 이유로 자동 짝짓지 않는다.

**완료 조건:** 새 문서 첫 저장, AI 원문의 첫 점자 저장, 동일 문장 두 블록, 삽입·분할·병합·재정렬, 삭제 저장 후 Undo, 응답 유실 후 재전송에서 원문·점자·메모가 같은 블록으로 다시 열린다. 저장 중 AI를 재호출하거나 크레딧을 차감하지 않는다.

## B-02. 부분 실패가 문서 쪽 수를 줄이지 않도록 수정

**근거:** [JobSaveService.java 41–77행](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/service/JobSaveService.java#L41).

현재 텍스트 작업(`mode=b`)의 `totalPages`를 **이번 요청에서 성공한 가장 큰 쪽 번호**로 덮어쓴다. 이미 3쪽이 저장된 문서에서 1쪽만 성공하고 2·3쪽이 실패하면 `totalPages=1`이 된다. 코드 경로에서 도출한 사례이며 운영 서버 재현 결과는 아니다.

요청에서 빠진 쪽의 `Page`·`PageResult` 등을 삭제하는 처리도 이 저장 경로에 없다. 의도적으로 쪽 수를 줄이는 경우도 숫자 변경과 실제 삭제를 구분해야 한다. 기존 테스트의 ‘새 작업에서 마지막 쪽 생성 실패’와 ‘기존 문서의 마지막 쪽 수정 실패’는 다른 상황이다.

**수정 제안:**

- 쪽별 트랜잭션은 유지하되 기존에 저장된 쪽은 이번 요청 실패만으로 전체 쪽 수에서 제외하지 않는다.
- 전체 스냅샷과 실패 쪽만 재전송하는 요청을 구분한다. 예: `saveMode: snapshot|patch`, 명시적 `deletedPageNos`. 필드명은 제안이다.
- 쪽 감소는 명시적 삭제 성공 뒤에 반영한다. 연관 결과·메모의 보존/삭제와 조회 목록을 일치시킨다. 빈 문서·마지막 쪽 삭제·중간 쪽 번호 유지/재배치도 정의한다.
- 원본 파일의 쪽과 조판된 점자 면은 다른 단위다. PDF/HWP/이미지의 원본 쪽을 이 기능으로 임의 변경하지 않는다.

**완료 조건:** 기존 3쪽 중 2·3쪽 수정 실패 후에도 기존 3쪽이 조회된다. 실패 쪽만 재전송해도 다른 쪽이 사라지지 않는다. 의도적 삭제 후 쪽 수·목록·본문·메모가 일치한다.

## B-03. 차단 블록을 저장 누락 때문에 삭제하지 않도록 수정

**근거:** [앱 JobClient.SaveAsync](https://github.com/Semojum/Semojum/blob/8e63161155b40c7e93de183886899b06fcec5faa/src/Semojum.Core/Integration/JobClient.cs#L143), [PageContentSaver.java 135–174행](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/result/service/PageContentSaver.java#L135).

앱은 `IsBlocked` 블록을 제외하고 저장 배열을 만든다. 서버는 전송된 패널 배열에서 빠진 기존 블록을 삭제한다. 같은 쪽에 정상·차단 블록이 섞여 있고 저장이 성공하면, 편집하지 않은 차단 블록까지 삭제 대상으로 해석될 수 있다. 메모도 전송 목록으로 교체된다. 이는 **앱과 서버 계약의 불일치**다.

**앱 수정(2026-10-20, C-01):** `JobClient.PlanSave`가 서버 id가 있는 모든 블록(차단 블록 포함)과 메모를 쪽 전체 스냅샷으로 보내고, 서버 id가 없는 실패/대기 표시 블록은 보내지 않으며, 점자 패널은 서버가 같은 id의 점자 항목을 보유한 것으로 기록된 블록만 보낸다(기록이 없으면 패널을 `null`로 두어 변경하지 않음). 블록을 모두 지운 쪽은 빈 배열로 보내 블록만 비우고, 쪽 삭제는 계약에 없음을 사용자에게 알린다. 성공 판정은 전송 계획의 쪽 수로 한다.

**수정 제안:** 앱은 유지할 차단 블록과 메모를 전체 스냅샷에서 누락하지 않게 수정한다. 서버는 `null` 패널=변경 없음, 빈 배열=명시적 전체 삭제의 의미를 문서화하고 차단 항목의 무단 수정·삭제를 검증한다. 다음 계약에서는 `deletedBlockIds`처럼 삭제 의도를 명시하고, 해당 계약 버전에서는 단순 누락을 삭제로 해석하지 않도록 맞추는 방식을 권장한다.

**완료 조건:** 정상 블록만 편집한 뒤에도 차단 블록·오류 정보·메모가 유지된다. 전체가 차단된 쪽도 보존된다. 사용자가 허용된 삭제를 명시했을 때만 삭제된다.

## B-04. 작업 상태 조회에도 소유자 검사 적용

**근거:** [JobController.java 137–162행](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/controller/JobController.java#L137), [JobService.getJobStatus](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/service/JobService.java#L591), [SecurityConfig](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/global/config/SecurityConfig.java#L42).

`GET /api/jobs/{jobId}/status`는 인증은 요구하지만 사용자 정보를 서비스에 전달하지 않는다. 서비스도 소유자 확인 없이 Redis 상태를 읽고 `touchForeground(jobId)`를 호출한다. 같은 컨트롤러의 SSE는 `findByIdAndUserId`로 검증한다.

**수정 제안:** 현재 사용자 ID로 소유자를 확인한 뒤 Redis 조회와 foreground 갱신을 수행한다. 다른 사용자 작업은 options/SSE와 동일한 정책으로 거부한다.

**완료 조건:** 본인 작업은 조회 가능하다. 타인 작업은 거부되며 상태 반환·foreground 갱신이 발생하지 않는다. 운영 환경에 대한 접근 재현은 수행하지 않았다.

## B-05. 메모 ID·해결 상태·위치 보존

**근거:** [replaceMemos](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/result/service/PageContentSaver.java#L208), [SaveMemo](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/dto/JobRequestDto.java#L82), [PageMemo](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/result/entity/PageMemo.java).

현재 쪽의 메모를 전부 지우고 새로 만든다. 요청의 `memoId`를 사용하지 않으며 `resolved` 필드가 없다. `elementId`가 그 쪽·패널에 속하는지, 줄 번호가 유효한지도 확인하지 않는다. 같은 메모를 다시 보내도 변경으로 처리된다.

**수정 제안:** `memoId`를 유지하는 갱신과 신규 메모의 `clientMemoId → memoId` 매핑을 제공한다. `resolved`를 저장·조회 DTO와 엔티티에 추가한다. 기존 생성 시각은 서버 값을 유지하고 실제 변경 시 수정 시각을 갱신한다. 신규 블록 ID 매핑을 적용한 뒤 메모 소속을 검증한다. 줄 편집으로 위치를 잃은 기존 메모는 미연결 상태 등으로 보존하는 규칙을 정의한다.

확인 사항(2026-10-20): `SaveMemo.createdAt`은 `java.time.LocalDateTime`이라 앱이 보내던 오프셋 포함 ISO 문자열(`…+09:00`)은 기본 Jackson 역직렬화에서 거부될 수 있다. 앱은 오프셋 없는 `yyyy-MM-ddTHH:mm:ss`로 보내도록 바꿨다(정적 검토 기준, 운영 재현 아님). 계약 확장 시 시각 형식과 시간대를 명세에 적어 두는 것을 권장한다.

**앱 변경:** 해결 상태를 전송하고 응답 ID를 반영한다. **완료 조건:** 반복 저장·재전송·다시 열기에서 ID·생성 시각·해결 상태를 유지한다. 다른 쪽/작업의 블록에 메모를 연결할 수 없다. 삭제·복원된 블록의 메모 처리도 B-01과 일치한다.

## B-06. 조판 옵션을 서버 저장·조회·양식에 일관되게 반영

**근거:** [LayoutOptions](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/dto/LayoutOptions.java), [validateLayoutOptions](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/service/JobService.java#L493), [앱 LayoutBody/ReadLayout](https://github.com/Semojum/Semojum/blob/8e63161155b40c7e93de183886899b06fcec5faa/src/Semojum.Core/Integration/JobClient.cs#L170).

| 앱 설정 | 현재 연동 상태 | 필요한 계약 확장 |
|---|---|---|
| 짝수 면 번호 | BE 허용값 `odd/every/none`; 앱도 짝수를 `none`으로 전송 | `even` 검증·저장·조회·앱 매핑 |
| 번호 출력 범위 | 서버 DTO와 앱 요청에 없음 | `numberRanges`, 범위의 기준 면 정의 |
| 번호와 독립적인 꼬리말 출력 면 | 서버 DTO와 앱 요청에 없음 | `footerPagePolicy` 등의 독립 정책 |
| 꼬리말 범위·내용·정렬 | 기본 `footerText`, `footerAlign`만 전달 | `footerRanges`, 빈 내용=숨기기, 끝 미지정=끝까지 |
| 마지막 짝수 면 보정 | 로컬 `PadToEven`만 사용 | `padToEven` 저장·조회 |

새 필드명은 제안이다. 범위의 면은 원본 쪽 번호와 구분해 조판된 점자 면 기준인지 명시하고 앱 정의와 맞춘다. DTO 외에 기본값·부분 갱신 merge·검증·작업 options 조회·계정 기본값·사용자 양식까지 왕복을 확인한다. 문서별 내용 범위와 재사용할 양식 기본값은 적용 대상을 구분한다.

**완료 조건:** `.smj`를 읽지 않고 서버 작업을 다시 열어도 설정을 복원하고 동일 앱/엔진에서 같은 조판 결과를 얻는다. 새 옵션이 없는 기존 작업·양식은 기존 기본값을 유지한다. 생략한 필드와 명시적으로 비운 범위를 구분한다.

## B-07. 저장 실패 사유와 입력 검증 보강

**근거:** [SaveJob/SaveJobPage](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/dto/JobRequestDto.java#L66), [JobSaveService](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/service/JobSaveService.java#L41).

현재 쪽 저장 예외는 모두 잡아서 `failedPages`에 번호만 넣는다. 루트 `@Valid`는 있지만 `pages`·패널 항목·메모까지 내려가는 중첩 `@Valid`가 없다. 중복 쪽 번호도 저장 루프에서 거부하지 않는다. 옵션은 본문보다 먼저 저장되므로 본문이 전부 실패해도 설정은 바뀔 수 있다.

**수정 제안:** 기존 응답을 유지하며 `pageResults[{pageNo,status,errorCode,retryable}]`, ID 매핑, `settingsSaved` 등을 추가한다. DB 예외 원문 대신 공개 오류 코드를 반환한다. 쪽 번호·중복 쪽·null 항목·필수값·패널·ID 소속을 검증한다. 요청 구조 오류는 쓰기 전에 거부하고 유효한 요청의 쪽별 실패는 부분 성공으로 응답한다. 한 쪽의 텍스트·점자·메모는 함께 롤백한다. 설정 먼저 저장하는 정책을 유지하면 응답에 표시한다.

**완료 조건:** 잘못된 ID와 일시적 장애를 구분한다. 중복 쪽을 두 번 처리하지 않는다. 모든 쪽 실패·일부 성공·설정만 성공을 정확히 표시한다. 타임아웃 재전송으로 블록·메모가 중복 생성되지 않는다(B-01/B-05).

## B-08. 실패 쪽 사용자 AI 재시도 API

**근거:** [JobController](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/controller/JobController.java), [PageWorker 내부 자동 retry](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/worker/PageWorker.java#L232). 내부 자동 재시도는 사용자 요청 API가 아니다.

**수정 제안:** `POST /api/jobs/{jobId}/retry` 같은 별도 API로 실패 쪽만 재처리한다. 대상 쪽 검증, 중복 요청 방지, DB/Redis/SSE 상태 전환, 취소와의 충돌, 기존 편집 보존을 정의한다. 전체 파일 재업로드로 대체하지 않는다.

**확정할 정책:** 재시도 허용 상태, 크레딧 차감/환불, 결과 교체 범위. 요청 전에 비용 정보를 제공해야 한다. **완료 조건:** 실패 쪽만 처리하고 성공 쪽 편집을 유지하며 요청 반복으로 큐 등록·과금이 중복되지 않는다.

## B-09. 암호 PDF와 HWP 추출 옵션

**근거:** [JobController 업로드 필드](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/job/controller/JobController.java#L45). 현재 multipart 요청에 PDF 암호와 HWP 추출 세부 옵션 필드가 없다.

**수정 제안:** 파일별 암호와 `PASSWORD_REQUIRED`/`INVALID_PASSWORD` 같은 오류를 정의하고 파일 검사·분리·썸네일·AI 원본 처리에 일관되게 적용한다. 암호는 로그·조회 응답에 노출하지 않는다. HWP/HWPX는 지원할 서식·추출 옵션과 기본값을 확정한 뒤 DTO와 변환기에 연결한다. 미정 옵션을 임의로 만들지 않는다.

**완료 조건:** 암호 없음·오류·정상을 구분해 복구한다. 여러 파일 중 실패한 파일을 식별한다. HWP 옵션은 실제 추출 결과로 검증하고 미지원 옵션을 조용히 무시하지 않는다.

## B-10. OCR 신뢰도·미지원 문자·검토 사유 전달

**근거:** [ResultService 신뢰도 저장](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/result/service/ResultService.java#L102), [PageResultSerializer](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/result/service/PageResultSerializer.java#L83), [quality_report](https://github.com/Semojum/BE/blob/5d8dda0a57f602a9ab7bc6f577d7bdb51bef8fc1/src/main/java/com/semojum/backend/domain/result/service/PageResultSerializer.java#L204).

쪽 평균·텍스트 요소의 OCR 신뢰도 저장 경로는 있지만 조회 직렬화에는 나오지 않는다. 반면 `quality_report.critical_errors/review_flags`와 `type/element_id/message`는 이미 제공된다. 오류 데이터 전체가 없는 것은 아니다. 앱도 기존 품질 보고서를 읽는 연결이 필요하다.

**수정 제안:** 기존 품질 보고서를 활용하고 필요한 신뢰도 필드의 범위·결측값 의미를 정의한다. 미지원 글자와 낮은 인식 신뢰도는 다른 상태로 표현한다. 문자 위치·블록·사유가 AI 응답에 있는지 확인하고 부족하면 AI 계약까지 확장한다. 앱의 추정값을 측정된 OCR 신뢰도로 표시하지 않는다.

**완료 조건:** 검토 사유가 해당 블록/위치에 연결된다. 신뢰도 없음과 낮은 신뢰도가 구분된다. 수동 수정 후 AI 원본의 검토 정보와 현재 편집 내용을 구분해 보여준다.

## 이미 제공되는 기능과 후속 범위

- **빈 문서 생성은 이미 존재한다.** `POST /api/jobs/text`가 AI 호출·크레딧 차감 없이 빈 작업을 만들고 `mode=b`는 첫 저장 때 쪽을 만든다. 빈 문서 API 신설이 B-01의 해결책은 아니다.
- **작업 취소도 이미 존재한다.** `POST /api/jobs/{jobId}/cancel`은 진행 중인 쪽을 마무리한 뒤 상태가 확정된다. 실패 쪽 사용자 재시도와 구분한다.
- **영어 문서·한영 혼용 규정·연속 공백의 점역 엔진 확장**은 별도 엔진 후속 범위다. BE 옵션 저장만으로 점역 규칙 적용이 완료되지 않는다.
- **관리자 웹·서버 폴더·휴지통**은 사용자가 후속으로 정했다. 기존 BE 기능의 존재 여부와 별개로 이번 저장 수정의 필수 범위에 넣지 않는다.
- **로그인 잠금 정책·운영 배포·실제 과금**은 운영 확인 대상이다. 이번 대조만으로 결함이나 검증 완료라고 단정하지 않는다.

## 작업 묶음과 인수 기준

1. **저장 정합성:** B-01/02/03/07. `SaveElement`, `SaveJob/Page`, `SaveResult`, `PageContentSaver`, `JobSaveService`, DB 제약과 앱 `JobClient.SaveAsync`를 함께 수정한다. 기존 클라이언트와 새 삭제 규칙의 계약 버전을 구분한다.
2. **권한:** B-04는 독립 수정 가능하다. status에 options/SSE와 같은 소유자 검사를 적용한다.
3. **편집 상태 보존:** B-05/06. 메모 DB 마이그레이션, 옵션 DTO/기본값/merge/양식, 앱 요청·복원과 `.smj` 호환성을 확인한다.
4. **추가 처리:** B-08/09/10은 상태·과금·추출·AI 응답 정책을 확정한 뒤 진행한다.

백엔드 단위 테스트 외에 실제 DB를 사용하는 저장→조회 통합 검증이 필요하다. 기존 `JobSaveServiceTest`에 **기존 문서 마지막 쪽 수정 실패**, `PageContentSaverTest`에 **첫 점자·ID 매핑·삭제 후 복원·메모 ID 유지·차단 블록 보존** 사례를 추가한다. 최종 인수는 앱의 생성→편집→명시적 저장→서버에서 다시 열기까지 확인한다. 자동 저장은 도입하지 않으며 로컬 `.smj`와 서버 저장 결과는 계속 구분한다.
