# shinshi 런처·패치

설계는 [런처·패치 설계](../docs/design/launcher-and-patch.md)를 따른다. .NET 10 SDK가 필요하다.

```powershell
dotnet build Launcher/Shinshi.Launcher.slnx
dotnet test Launcher/Shinshi.Launcher.slnx
```

| 프로젝트 | 역할 |
|---|---|
| `src/Shinshi.Patch.Core` | 매니페스트·채널 포인터 형식, Ed25519 서명 봉투, 경로 검사, 해시, 빌드 신원 읽기 |
| `src/Shinshi.Release.Packager` | 서명 키 생성, Unity 빌드를 게시 폴더로 만드는 콘솔 도구 |
| `src/Shinshi.Patch.Client` | 설치·업데이트·검증·복구 엔진(런처 화면이 쓴다) |
| `src/Shinshi.Launcher.Account` | 로그인(아이디·비밀번호), 자동 로그인(DPAPI 갱신 토큰), 게임 세션 발급, Named Pipe 세션 전달 |
| `src/Shinshi.Launcher.UI` | WPF 런처 화면. `launcher.json`의 `apiBaseUrl`(로그인·게임 시작), `websiteUrl`(회원가입 링크)을 읽는다 |
| `tests/Shinshi.Launcher.Tests` | 위 프로젝트(화면 제외)의 단위 시험. Windows 전용(net10.0-windows) |

런처는 시작할 때 업데이트가 있으면 로그인 여부와 무관하게 바로 받고, 자동 로그인은 그와 동시에 진행한다. 자동 로그인 갱신 응답을 잃어도(시간 초과, 502·504) 저장된 토큰은 지우지 않고 다음 자동 로그인에서 다시 시도하며, 서버가 401로 거절할 때만 지운다.

서비스 상태의 공지 제목(`notice`)이 비어 있으면 `notices` 목록에서 제목이 있는 Launcher 또는 Both 대상의 첫 공지를 표시한다.

런처 세션을 받은 클라이언트는 Release 로비·캐릭터 선택/생성 흐름을 거쳐 마을 입장권을 요청한다. 신규 직업 생성 UI와 API 기본 허용 직업(0,1,2)의 공개 정책 대조는 남아 있다. `GameSessionRefresher`가 만료 5분 전 게임 세션을 회전하며, 회전 응답 유실 시 기존 토큰 재사용 복구는 하지 않고 재로그인을 안내한다. 실제 런처 경유 전체 흐름·다인 접속은 미검증이다.

Windows 독립 실행 배포: `dotnet publish Launcher/src/Shinshi.Launcher.UI -c Release -r win-x64 --self-contained true -o <배포폴더>`. 해당 폴더에 `launcher.json`을 넣고 전체를 함께 배포한다. 개발 빌드는 `channel: internal`, `teamBuild: true`로 설정한다. 같은 PC의 데디서버가 설치를 막지 않도록 실행 중 검사는 런처 설치 루트 아래 게임으로 한정한다.

## 설치 EXE 배포

```powershell
./Launcher/scripts/Build-Installer.ps1 -ConfigPath <launcher.json> -OutputDirectory <출력폴더>
# LAN 테스트용 CA가 필요한 teamBuild에서만 추가한다.
./Launcher/scripts/Build-Installer.ps1 -ConfigPath <launcher.json> -CertificatePath <공개-CA.cer> -OutputDirectory <출력폴더>
```

출력 `Shinshi-Setup.exe`를 홈페이지에서 제공한다. 인증서가 있으면 설치 화면에서 대상·SHA-256 지문을 알린 뒤 현재 사용자 Root 저장소에 등록한다. 공개 인증 HTTPS 배포에서는 CertificatePath를 생략한다. 이 인증서는 TLS 연결용이며 EXE Authenticode 서명이나 SmartScreen 평판을 대신하지 않는다.

설치 파일은 `%LOCALAPPDATA%/Shinshi/launcher/versions/{패키지해시}/`에 `ShinshiLauncher.exe`와 `launcher.json` 두 개만 배치하고 바탕화면에 `신시 런처.lnk`를 만든다. 게임(`game/`), 토큰(`Account/`), 로그(`logs/`)는 기존 경로를 유지한다. 기존 ZIP 폴더를 지우거나 계정 파일을 이동하지 않는다. 재설치 시 동일 패키지는 복구하고 다른 패키지는 별도 폴더에 설치해 기존 파일을 보존한다. 런처 자체 자동 업데이트 기능은 별도다.

`Shinshi-Setup.exe --verify-only`는 변경 없이 내장 ZIP·인증서 유효성을 검사한다. 자동 설치 검증은 `--accept-certificate-sha256 <승인한-인증서-SHA256>`으로 수행하며 지문이 다르면 설치하지 않는다. 일반 사용자는 인자 없이 실행한다.

아이콘 원본은 `src/Shinshi.Launcher.UI/Assets/launcher.png`, Windows 다중 해상도 아이콘은 같은 폴더의 `launcher.ico`다. 재변환은 `scripts/Convert-LauncherIcon.ps1`을 사용한다. EXE·창·상단 로고·바로가기가 같은 아이콘을 사용한다. 빌드 중간 산출물은 Git 제외된 `.local/installer/`에 둔다.

## 로컬 게시

Unity 빌드(`Shinshi/Build/...` 메뉴)의 `BuildIdentityWriter` 후처리는 끝날 때 `Shinshi_Data/StreamingAssets/build-identity.json`을 쓴다. 게시 도구는 이 파일이 없으면 게임을 게시하지 않는다.

키와 게시 폴더는 저장소 밖에 둔다. 개인키 파일은 커밋하지 않고 공유하지 않는다.

```powershell
dotnet run --project Launcher/src/Shinshi.Release.Packager -- keygen --out D:/ShinshiRelease/keys
dotnet run --project Launcher/src/Shinshi.Release.Packager -- publish --build Builds/windows-client --product game --channel internal --sequence 1 --key-file D:/ShinshiRelease/keys/shinshi-patch-private.key --out D:/ShinshiRelease/publish
```

- `--sequence`는 같은 채널에서 이전 게시보다 커야 한다.
- `--min-launcher <버전>`으로 매니페스트의 최소 런처 버전을 지정할 수 있다. 자체 업데이트 실행 기능과는 별개다.
- `stable` 채널은 Release 빌드만 받는다.
- 빌드 루트의 `*_BackUpThisFolder_ButDontShipItWithYourGame`, `*_BurstDebugInformation_DoNotShip`는 게시물에서 뺀다.

게시 폴더 배치는 `{product}/channels/{채널}.json`, `{product}/releases/{releaseId}/manifest.json`, `{product}/blobs/{sha 앞 2자}/{sha}`다. 런처는 설정에 따라 로컬 `FileSystemPatchSource` 또는 `HttpPatchSource`를 선택한다(`MainViewModel.CreateClient`). 이 게시 폴더를 NAS에 올려 제공할 수 있지만 실제 공개·다운로드 수용은 미검증이다. 외부 체험자가 받으려면 NAS를 HTTPS로 열어야 한다(공개 방식 미정).
