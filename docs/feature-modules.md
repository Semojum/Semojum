# 데스크톱 기능 프로젝트

`Semojum.Desktop`은 애플리케이션 창, 화면 전환, Windows 실행 환경을 조합하는 호스트다. 문서 모델과 공통 프로토콜은 `Semojum.Core`에 두고 기능별 동작과 화면은 다음 프로젝트에 둔다.

| 기능 | 처리·상태 | WPF 화면과 자산 |
| --- | --- | --- |
| 계정 | `Semojum.Feature.Account` | `Semojum.Feature.Account.Wpf` |
| 게시판 | `Semojum.Feature.Board` | `Semojum.Feature.Board.Wpf` |
| 점자 규정 | `Semojum.Feature.Rules` | `Semojum.Feature.Rules.Wpf` |
| 설정·양식 | `Semojum.Feature.Settings` | `Semojum.Feature.Settings.Wpf` |
| 기관 관리 | `Semojum.Feature.Institution` | `Semojum.Feature.Institution.Wpf` |
| 문서 편집 | `Semojum.Feature.Editor` | `Semojum.Feature.Editor.Wpf` |
| 문서 열기·저장·인쇄 | `Semojum.Core`의 문서 모델 | `Semojum.Feature.Documents.Wpf` |
| 공지·공개 문의 | `Semojum.Core`의 API 모델 | `Semojum.Feature.Public.Wpf` |
| 마이페이지 | `Semojum.Core`·게시판 모델 | `Semojum.Feature.Usage.Wpf` |
| 도움말·정보·화면 설정 | `Semojum.Core`의 화면 설정 모델 | `Semojum.Feature.Shell.Wpf` |

`Semojum.UI.Common`은 공통 대화상자, 창 프레임과 위치, 버튼과 입력 표시를 제공한다. 기능 화면은 공통 창을 사용하고, 다른 화면을 여는 동작은 호스트가 전달하는 콜백으로 연결한다. 로그인과 기관 관리는 `IEmbeddedScreenHost`를 통해 앱의 기존 창 안에 표시된다. `Documents.Wpf`는 양식 선택 화면을 위해 `Settings.Wpf`를, 쪽 이동 계산을 위해 `Feature.Editor`를 참조한다. 마이페이지는 게시글 데이터 타입을 위해 `Feature.Board`를 참조한다. 게시판 목록은 요청 실패나 취소 시 이전에 성공한 결과를 유지한다.

프로젝트별 변경 중에는 영향을 받은 프로젝트만 빌드할 수 있다. 예를 들어:

```powershell
dotnet build src/Semojum.Feature.Board.Wpf -c Release
dotnet build src/Semojum.Feature.Editor.Wpf -c Release
```

앱 통합과 실행 확인에는 호스트를 다시 빌드한다. WPF 화면별 빠른 검사 방법과 검사 범위는 [데스크톱 검사](desktop-checks.md)를 참고한다. 현재 화면 검사 실행기는 호스트 프로젝트에 있으므로 개별 기능 프로젝트의 독립 빌드와 화면 검사는 별개다. 공통 UI나 호스트 조합이 바뀌거나 배포하기 전에는 영향을 받는 화면과 전체 통합 검사를 실행한다.

서버와 점역 엔진은 이 데스크톱 프로젝트에 포함하지 않는다. 기존 데이터 형식과 서버 계약은 이 구조 변경으로 바꾸지 않는다.
