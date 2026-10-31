# 데스크톱 기능 프로젝트

`Semojum.Desktop`은 WPF 창과 화면을 연결하는 앱 호스트다. 문서 모델과 공통 프로토콜은 `Semojum.Core`에 두고, 기능별 처리와 상태는 다음 프로젝트에 둔다.

| 프로젝트 | 현재 책임 |
| --- | --- |
| `Semojum.Feature.Board` | 게시판 요청·응답과 목록 상태 |
| `Semojum.Feature.Board.Wpf` | 게시판 목록의 WPF 표시 |
| `Semojum.Feature.Rules` | 점자 규정 검색 요청·응답 |
| `Semojum.Feature.Settings` | 점역 기본값·사용자 양식 요청·응답 |
| `Semojum.Feature.Institution` | 기관 관리 요청·응답 |
| `Semojum.Feature.Institution.Wpf` | 기관 상태·증빙 표시 변환 |
| `Semojum.Feature.Editor` | 문서 위치·줄·메모 이동 계산 |
| `Semojum.Feature.Editor.Wpf` | 점자 편집 패널·편집 버퍼·검토 표식과 패널별 쪽 이동 표시 |
| `Semojum.Feature.Account` | 로그인 권한·세션 복원 정책과 자격 증명 저장 인터페이스 |
| `Semojum.UI.Common` | 공통 WPF 입력·버튼·대화상자 표시 요소 |

각 기능 프로젝트는 `Core`를 참조하며 서로를 직접 참조하지 않는다. 앱 호스트가 필요한 기능 프로젝트를 조합한다. 게시판 목록의 ViewModel은 서버 요청이 실패하거나 취소되면 이전에 성공한 목록을 유지한다. Windows 자격 증명 저장 구현은 데스크톱 호스트에 남고 계정 기능의 저장 인터페이스를 구현한다.

게시판 목록, 기관 상태 표시와 점자 편집 패널을 제외한 주요 WPF 화면과 공통 창은 여전히 `Semojum.Desktop`에 있다. 새로운 기능별 UI를 옮길 때 공통 창·리소스 의존을 먼저 분리해야 하며, 프로젝트 이름만 나눈 것으로 모든 화면이 독립된 것은 아니다. 편집기 WPF 프로젝트는 기존 호스트 자산을 참조하고 호스트에 내부 접근을 허용하므로, 향후 자산과 호스트 경계도 분리해야 한다. 기존 서버 및 점역 엔진 구현은 이 프로젝트에 포함하지 않는다.

변경한 영역을 빠르게 확인하는 방법은 [데스크톱 검사](desktop-checks.md)를 참고한다. 앱을 배포하기 전에는 전체 통합 검사를 함께 실행한다.
