# 세모점 Windows 편집기

[Service 서비스 기획](https://app.notion.com/p/3a643813bc2e801aa2b9eaf48dde8f2e)을 최우선으로, Notion V3.1 API·기능정의서 V2를 대조하여 기존 WPF 시안에 실제 점역 엔진과 작업 파일·서버 연결을 구현하고 있습니다. **전체 설계 구현이 끝난 상태는 아닙니다.** 2026-10-27 QA 4차 반영과 검증 결과는 [QA 4차 결과](docs/qa4-2026-10-27.md)에 구분합니다.

기준 문서 충돌의 적용 순서는 [서비스 기획 우선 적용](docs/service-priority.md)에 기록합니다.

최신 상태: [2026-10-28 Figma 반영·검증](docs/figma-alignment-2026-10-28.md) · [54개 화면 비교](docs/visual-review/README.md) · [남은 작업](docs/remaining-work.md). 전체 화면의 픽셀 일치 검토는 계속 진행 중입니다.

## 실행

Windows와 .NET 10 SDK가 필요합니다. 개발 중에는 `semojum-braille` v3.4.41이 설치된 Python 경로를 지정합니다.

```powershell
$env:SEMOJUM_ENGINE_PYTHON = 'C:\path\to\venv\Scripts\python.exe'
dotnet run --project src/Semojum.Desktop -c Release
```

배포 ZIP은 `Semojum.Mockup.exe`와 `engine` 폴더를 포함합니다. 실행 파일 이름은 기존 배포와의 호환을 위해 유지합니다. 배포 폴더 전체를 사용해야 하며 Python·.NET 별도 설치는 필요하지 않습니다. 엔진 파일이 없거나 시작에 실패하면 오류를 표시합니다. 샘플 점자로 대신 채우지 않습니다.

macOS/Linux에서는 핵심 로직 검사와 Windows 교차 빌드만 할 수 있습니다. WPF 실행은 Windows에서 검증합니다.

## 사용 흐름

- **새 문서 / 텍스트 열기**: AI와 크레딧 없이 빈 서버 작업을 생성하고, 묵자 입력·TXT를 로컬에서 점역합니다. TXT의 긴 원문은 엔진 입력 제한에 맞춰 나누며 원본 파일을 보존합니다. 새 블록도 안정적인 클라이언트 ID로 원문·점자를 짝지어 저장합니다.
- **도움말 → 메뉴별 기능 → 추가 도구 → 로그인** 후 **파일 → 열기**: 문서를 서버에 보내고 완료된 쪽의 묵자를 받아 로컬에서 점역합니다. 기존 작업은 추가 도구의 서버 작업 열기에서 작업 ID로 엽니다. 변환 중단은 서버에 취소 요청을 보냅니다.
- **모눈 직접 편집**: 내보내기와 같은 판면에서 점자 입력·드래그 선택·블록 이동·실행 취소를 제공합니다. 원문은 기본 화면에서 같은 원본 쪽의 블록을 연속으로 표시합니다.
- **판면·꼬리말**: 변경선, 번호 전체/홀수/짝수/지정 범위, 독립 꼬리말과 범위별 정렬을 설정합니다. 새 설정은 `.smj` v4로 저장하며 v1–v3도 열 수 있습니다.
- **6점 입력**: F6으로 켜고 끕니다. 기본 `F D S J K L`. 점자를 직접 고치면 표시 색이 바뀌며 재점역 전에 유지/덮어쓰기/취소를 선택합니다.
- **Ctrl+S**: 버전 있는 ZIP `.smj`에 저장하고, 서버 작업이 연결되어 있으면 서버 저장도 요청합니다. 서버 부분 실패는 따로 표시하며 로컬 파일은 남습니다. 쪽 이동과 종료 때 자동 저장하지 않습니다.
- **파일 → 범위 지정 저장**: 전체 또는 원본 쪽 범위의 BRF·묵자 TXT·점자 TXT. BRF와 점자 TXT는 조판 결과의 면 범위도 선택할 수 있으며 쪽번호와 꼬리말을 유지합니다. 변환되지 않은 쪽이 포함되면 완료된 원본 쪽 범위를 선택하도록 안내합니다.
- **메모**: 마지막으로 편집한 패널의 현재 줄에 작성합니다. `.smj`와 서버 저장 본문에 포함됩니다.
- **창 분리**: 원본·텍스트·점자 패널을 각각 독립 창으로 옮길 수 있습니다. 남은 패널이 메인 창을 채우며, 분리 창을 닫으면 편집 내용을 유지한 채 돌아옵니다.
- **찾기·바꾸기**: Ctrl+F/Ctrl+H로 활성 텍스트 또는 점자 패널의 전체 블록을 검색하고 F3으로 다음 결과로 이동합니다. 잠긴 점자는 바꾸지 않으며, 텍스트를 바꾼 블록은 로컬 엔진으로 재점역합니다.

신규 블록·첫 점자·받은 작업 사본·부분 실패 재시도는 최신 저장 계약에 맞췄습니다. 사용자 제공 계정으로 실제 API 인증, 텍스트·메모·점자 저장/재열기, 한 쪽 이미지 AI 변환과 저장, 규정 340건을 확인했습니다. 자동 검사와 운영 서버 검사 범위, 남은 제약은 [QA 4차 결과](docs/qa4-2026-10-27.md)를 참고하세요.

## 구성

| 경로 | 역할 |
|---|---|
| `src/Semojum.Core/Integration` | JSONL 엔진, 인증, V3.1 작업 API |
| `src/Semojum.Core/SmjFile.cs` | 원본·편집 상태·설정·메모 ZIP 보존, 원자적 교체 |
| `src/Semojum.Desktop` | WPF 편집기·공유 편집 버퍼·계정/작업 화면 |
| `tests/Semojum.Core.Tests` | 실행 가능한 핵심·프로토콜·저장 검사 |
| `tests/Semojum.Live.Tests` | 별도 승인을 받은 계정·검사용 파일로 실행하는 실서버 검사 |
| `scripts/editor_composition.py` | 실제 엔진 함수를 재사용하는 앱 조판·셀 좌표 브리지 |
| `scripts/build-engine.ps1` | 고정 엔진 커밋을 Windows 실행 파일로 묶기 |
| `scripts/publish.ps1` | .NET과 엔진을 함께 포함한 Windows x64 ZIP |

## 검증과 패키징

```powershell
dotnet build Semojum.sln -c Release
dotnet run --project tests/Semojum.Core.Tests -c Release
./scripts/publish.ps1
./artifacts/Semojum-win-x64/Semojum.Mockup.exe --smoke-test ./artifacts/smoke
```

`SEMOJUM_ENGINE_PYTHON`이 있으면 실제 v3.4.41 테스트를 추가 실행하고, 없으면 그 검사는 SKIP으로 표시합니다. `--smoke-test`는 네트워크와 분리된 고정 샘플로 WPF 창·편집 버퍼·저장을 검사합니다. Windows CI는 실제 엔진 검사와 UI 검사를 별도로 실행합니다. 물리 키보드 동시 입력, 한국어 IME, 보조기기와 여러 DPI는 별도 장비 검증이 필요합니다.

[엔진 계약과 문서 차이](docs/engine-contract.md) · [.smj 규격](docs/smj-format.md) · [외부 라이브러리 고지](THIRD-PARTY-NOTICES.md)
