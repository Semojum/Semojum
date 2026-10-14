# 로컬 점역 엔진 계약과 설계 차이

확인일: 2026-10-14. 사용자 결정: Notion 가이드와 다르면 공개된 실제 엔진을 기준으로 구현하고 차이를 이 문서에 남긴다.

기준 소스는 [Semojum/braille v3.4.17](https://github.com/Semojum/braille/tree/74d6758bf34cdb028c6121c7ebe7fb568ad35fec)이다. `semojum_braille/sidecar.py`, `assist.py`, `docs/sidecar.md`를 확인했다. BE→AI의 [braille_service.proto](https://app.notion.com/p/braille_service-proto-42e43813bc2e830e804901f067f7cdd8)는 로컬 JSONL 프로토콜과 구분한다.

| 항목 | Notion 가이드 | v3.4.17 실제 계약 / 클라이언트 처리 |
|---|---|---|
| 한글·영문 급수 | `korean_grade`, `english_grade` 정수 | `korean_grade1`, `english_grade1` 불리언. API의 grade1/grade2를 변환한다. |
| 표 | 일반 translate와 표 옵션 | 별도 `op: table`, `table_source`, `render_mode`, `tn_text`, `box_level`. 구조 없는 표를 임의로 평문 점역하지 않는다. |
| BRF | `sources`, `opts`, `footer`, `footers` | `op: brf`, `job.pages`, `job.options`, `footer_braille`, `footers_braille`, `start_braille_page`. |
| 기호 조회 | symbols 명령 | 제공하지 않는다. 지원 API인 것처럼 호출하지 않는다. |
| 오류 | `code`, TOO_LONG 등 | 대체로 `error` 문자열과 요청 id. 없는 오류 코드를 엔진 응답으로 표시하지 않는다. |
| 입력 길이 | 1,000자 상한 | 코드에는 해당 하드 제한이 없다. 프로세스 응답 제한과 취소를 적용한다. |
| 선택지 합침 | merge_choices | 로컬 엔진에 옵션 없음. 계정/작업 옵션은 보존하며 로컬 지원으로 표시하지 않는다. |
| 태그 검증 | unknown_tags 결과 | 해당 응답 필드 없음. 드롭된 문자와 blocked만 실제 결과로 표시한다. |
| 영어 규정·혼용·공백 | 여러 점역 옵션 | JSONL에서 지원하는 급수 플래그만 전달. 지원되지 않는 옵션은 조용히 성공했다고 표시하지 않는다. |
| 검수 등급 | 품질 지표로 오해 가능 | grade는 확인 우선순위. ‘먼저 확인/확인/나중에 확인’이며 정확도나 검수 완료가 아니다. |

## 실행과 장애 처리

UTF-8 한 줄 JSON을 표준입력으로 보내고 요청 id가 같은 한 줄 응답을 받는다. stdout은 프로토콜 전용이고 stderr는 별도로 계속 비운다. 시작 시 ping의 protocol=1을 검증한다. 취소·시간 초과·잘못된 응답·프로세스 종료 후에는 프로세스를 정리하고 다음 요청에서 다시 시작한다. 엔진 실패 시 가짜 점자나 고정 샘플을 반환하지 않는다.

급수 변경, 원문 변경, 직접 수정, 재점역 요청이 이전 응답을 무효화한다. 수동 점자 덮어쓰기는 사용자 선택을 받는다. 직접 수정한 블록에는 이전 음절 경계 정보를 재사용하지 않는다.

BRF 내보내기는 엔진의 실제 조판기를 사용한다. 원시 점자와 음절 경계는 `.smj`에 보존한다. 표 구조를 편집된 화면 문자열에서 역추정하는 기능은 없다. 표 편집 UI/구조 갱신 규칙은 추가 구현이 필요하다.

## 검증 범위

실제 v3.4.17로 한글 정자/약자, 누락 문자, 표 초안, 꼬리말 로마자, 검수 순서, BRF 생성/읽기를 테스트했다. 이것이 한국어 전체·수학·모든 표의 점역 정확도를 보증하지는 않는다. 엔진의 라이선스와 `NOTICE.md`, `third_party` 고지는 배포물에 함께 제공한다. kiwi는 설치하거나 번들하지 않는다.
