# 백엔드 수정 목록(B-01~B-10) 반영 확인 — 2026-10-28

2026-10-20 검토서 「데스크톱 편집기 연동을 위한 백엔드 수정 목록」(B-01~B-10)이 모두 반영됐다는 전달을 받아, `Semojum/BE` `dev` 최신 커밋 `5e2acc4`(2026-10-28 기준, `work/BE-reference` 체크아웃을 fast-forward)의 코드와 `CLAUDE.md` 규칙을 다시 대조하고 앱을 맞췄다. 정적 코드 대조이며 운영 서버 호출·배포 확인은 아니다. BE 코드는 변경하지 않았다.

## 항목별 결과

| 번호 | BE 반영 (dev 5e2acc4) | 앱 상태 | 이번 수정 |
|---|---|---|---|
| B-01 블록 ID | 반영. 앱이 만든 id(`[A-Za-z0-9_-]{1,64}`)를 두 패널이 공유하고, 없는 id는 새 블록, 지운 id는 복원, 텍스트 id로 온 첫 점자를 같은 id로 생성 | `JobSaving.BlockId = ServerId ?? ClientId`로 이미 일치 | 없음 |
| B-02 쪽 수 | 반영. 쪽 수 = 남아 있는 마지막 쪽 번호. 줄이는 건 본문 `totalPages`를 보낼 때만(PDF·이미지는 400) | 텍스트 작업만 `totalPages` 전송, 재전송에는 생략 | 없음 |
| B-03 차단 블록 | 반영. 차단 블록은 배열에서 빠져도 지우지 않고 원래 자리에 남김 | ServerId가 있는 차단 블록은 포함, 서버 블록이 없는 실패 표시 블록만 제외 | 없음 |
| B-04 상태 조회 소유자 | 반영. `JobService.getJobStatus`가 `findByIdAndUserId` → COMMON4003 | 영향 없음(타인 작업은 열지 않음) | 없음 |
| B-05 메모 ID·해결 | 반영. `memoId`로 갱신, 새 메모는 `clientMemoId` → 응답 `pageResults[].memoIds[{clientMemoId, previousMemoId, memoId}]`, `resolved`·`resolvedAt`·`author`·`replies` 저장 | 해결·작성자·답글은 이미 전송. `clientMemoId`와 응답 매핑은 없었음 | 새 메모에 `clientMemoId=LocalId` 전송, 응답 `memoIds`를 읽어 메모 `ServerId` 갱신(사본 작업의 `previousMemoId` 재발급 포함). 테스트 `MemoIds` |
| B-06 조판 옵션 | 부분 반영. `footerPages`(odd/every, 2026-10-27) 추가. `pageNumberLine`은 여전히 odd/every/none(짝수 없음), 번호 범위·꼬리말 범위·마지막 짝수 면 보정은 DTO에 없음 | 꼬리말 출력 면을 .smj 전용으로 취급 | `footerPages` 전송·조회·양식 지원 범위 갱신(`ServerContract.FooterPages`). 짝수 페이지·번호 범위·꼬리말 범위·짝수 면 보정은 계속 .smj에만 보존하고 안내 |
| B-07 저장 검증·사유 | 반영. 중첩 `@Valid`, 중복 쪽 400, `pageResults[].errorCode`(공개 코드, 알 수 없으면 COMMON5000) | `pageResults` 검증·표시 이미 있음 | 없음 |
| B-08 실패 쪽 재변환 | 반영. `POST /api/jobs/{jobId}/retry`(사유 없이 막힌 쪽만, 취소·크레딧 소진 제외, 성공 시 그때 차감)와 `POST /resume`(CREDIT_EXHAUSTED 쪽만, 기획 10-1). CREDIT4001·JOB4014·JOB4015 | 두 버튼이 비활성(미지원 안내) | `JobClient.RetryAsync/ResumeAsync` 추가. 부분 변환 실패 창 「실패한 쪽 다시 변환」, 크레딧 띠 「이어서 처리」, 연결 끊김 창 「이어서 변환」(상태 재조회·미수신 쪽만 가져오기)을 연결. 다시 받은 쪽만 제자리에서 교체하고 다른 블록의 편집은 유지. 테스트 `Restart` |
| B-09 암호 PDF·HWP 옵션 | **미반영**. 업로드 multipart에 암호·HWP 옵션 필드 없음(`JobController.createJob`) | 암호 PDF 창(207:4747)·스캔 PDF 확인(207:4780) 미구현 | 없음 — 계약 확정 후 연결 |
| B-10 OCR 신뢰도 | 반영. 텍스트 요소 `ocr_confidence`, `quality_report.ocr_confidence_avg`(소수 셋째 자리, AI가 0이면 null = 값 없음) | 품질 보고서는 표시, 신뢰도는 없었음 | `BlockMetadata.OcrConfidence`·쪽 평균을 읽어 .smj에 보존, 쪽별 변환 상태에 「OCR 신뢰도」 열(평균, 0.7 미만은 「낮음」, 없으면 「값 없음」). 인식 불가 문자(■) 안내와는 별개 상태로 둔다 |

그 밖의 BE 변경 중 앱과 맞닿는 것: 끝난 지 1시간이 지난 작업의 `status`가 DB로 답한다(앱의 재열기 폴링 그대로 동작), 취소로 막힌 쪽에 `blocked_reason=CANCELED`(SSE), 계정 기본 출력용 설정 `GET/PUT /api/users/output-options`(앱의 출력 설정은 아직 로컬 선호값만 사용 — 연결은 남은 작업).

## 점역 엔진 v3.4.41

`scripts/build-engine.ps1`을 `v3.4.41`(`429ee6bc`)로 올리고 핸드셰이크 검사도 3.4.41로 바꿨다. 3.4.38부터 사이드카 `translate`/`table`의 `cells`에 요소 앞뒤 빈 줄이 기본으로 실리므로, 앱은 `blank_lines: false`를 보내 편집 화면용 요소 본문을 그대로 받는다(쪽 조판은 `build_brf`가 한다). 3.4.40의 `dropped` 여섯 갈래(한자·결합 부호·수식 명령 추가)는 같은 모양이라 기존 파서로 읽힌다. 3.4.41은 여러 줄 요소의 둘째 줄 들여쓰기 회귀를 고친 판이며 업스트림이 「이 판 이상을 쓰라」고 안내한다.

| 검사 | 결과 |
|---|---|
| 엔진 동결 빌드(PyInstaller 6.22.3, Python 3.13.6) | 성공, `{"version":"3.4.41","protocol":1}` |
| 코어 + 동봉 엔진 v3.4.41 | 80/80 (기존 78 + `MemoIds`, `Restart`). Python 경로 검사 1개 SKIP |

## 남은 결정

- B-09: PDF 암호·HWP 추출 옵션 계약(필드·오류 코드·재입력 정책)이 정해지면 Figma 207:4747·207:4780 창을 업로드에 연결한다.
- B-06 나머지: 짝수 페이지행, 번호 출력 범위, 꼬리말 범위·범위별 정렬, 마지막 짝수 면 보정은 서버 저장 계약이 없어 .smj 전용이다.
- 재변환·이어서 처리의 운영 검증: 실제 서버에서 실패 쪽·크레딧 소진 작업을 만들어 `retry`/`resume` 흐름과 차감(성공 쪽만)을 확인하지 않았다. 새 작업 생성·크레딧 소모가 필요하므로 허용 범위를 정한 뒤 실행한다.
- 출력용 설정 계정 기본값(`/api/users/output-options`) 연결.
