# .smj 파일 연결

이 배포본은 압축을 푸는 방식입니다. 보관할 폴더에 ZIP을 풀고 `Semojum.Mockup.exe`, `register-smj.ps1`, `smj-association-core.ps1`을 같은 폴더에 둡니다. 폴더를 이동하거나 삭제하면 등록된 경로가 더 이상 작동하지 않습니다.

현재 Windows 계정에서 PowerShell을 열고 해당 폴더로 이동해 다음을 실행합니다. 관리자 권한은 필요하지 않습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\register-smj.ps1 -Action Register
```

세모점이 `.smj`의 **연결 프로그램 후보**에 추가됩니다. 기존 기본 앱은 변경하지 않습니다. `.smj` 파일을 두 번 클릭해 세모점으로 열려면 파일의 **연결 프로그램**에서 세모점을 선택하고 항상 사용하도록 지정하거나, Windows 설정의 기본 앱에서 `.smj` 기본 앱을 선택하세요. Windows가 기존 사용자의 선택을 관리합니다.

연결을 제거하려면 **등록할 때와 같은 폴더**에서 다음을 실행합니다. 이미 실행 파일을 삭제했더라도 스크립트 두 개는 같은 폴더에 남아 있어야 합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\register-smj.ps1 -Action Unregister
```

등록한 경로가 현재 연결과 다르면 다른 설치를 보호하기 위해 아무것도 제거하지 않습니다. 현재 기본 앱으로 세모점을 직접 선택했던 경우 제거 후 Windows 기본 앱 설정에서 다른 앱을 선택하세요. 스크립트는 다른 앱의 설정이나 Windows가 관리하는 사용자 기본 앱 선택을 수정하지 않습니다.

배포 폴더를 옮기려면 기존 폴더에서 연결을 제거하고 새 폴더에서 다시 등록하세요. 앱 자체 업데이트는 원래 실행 파일을 통해 새 버전을 실행하며, `.smj` 경로도 그대로 전달합니다. 업데이트 후에도 원래 배포 폴더를 유지해야 합니다. 이미 실행 중인 창으로 파일을 전달하는 기능은 제공되지 않아 새 프로세스가 열릴 수 있습니다.
