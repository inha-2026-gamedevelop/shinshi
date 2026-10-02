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

서비스 상태의 공지 제목(`notice`)이 비어 있으면 `notices` 목록에서 제목이 있는 Launcher 또는 Both 대상의 첫 공지를 표시한다.

런처가 넘긴 게임 세션을 받은 Bootstrap 클라이언트는 계정의 캐릭터 목록을 조회하고 선택한 캐릭터로 마을 입장권을 요청한다. 캐릭터가 없는 계정은 테스트용 검사 캐릭터를 생성할 수 있다. 현재 화면은 접속 검증용이며 완성된 로비나 마을 플레이 화면은 아니다. 게임 세션 갱신은 아직 연결하지 않아 세션 만료 후 새 입장을 요청하려면 게임을 닫고 런처에서 다시 시작해야 한다.

Windows 독립 실행 배포: `dotnet publish Launcher/src/Shinshi.Launcher.UI -c Release -r win-x64 --self-contained true -o <배포폴더>`. 해당 폴더에 `launcher.json`을 넣고 전체를 함께 배포한다. 개발 빌드는 `channel: internal`, `teamBuild: true`로 설정한다. 같은 PC의 데디서버가 설치를 막지 않도록 실행 중 검사는 런처 설치 루트 아래 게임으로 한정한다.

## 로컬 게시

Unity 빌드(`Shinshi/Build/...` 메뉴)는 끝날 때 `Shinshi_Data/StreamingAssets/build-identity.json`을 쓴다. 게시 도구는 이 파일이 없으면 게임을 게시하지 않는다.

키와 게시 폴더는 저장소 밖에 둔다. 개인키 파일은 커밋하지 않고 공유하지 않는다.

```powershell
dotnet run --project Launcher/src/Shinshi.Release.Packager -- keygen --out D:/ShinshiRelease/keys
dotnet run --project Launcher/src/Shinshi.Release.Packager -- publish --build Builds/windows-client --product game --channel internal --sequence 1 --key-file D:/ShinshiRelease/keys/shinshi-patch-private.key --out D:/ShinshiRelease/publish
```

- `--sequence`는 같은 채널에서 이전 게시보다 커야 한다.
- `stable` 채널은 Release 빌드만 받는다.
- 빌드 루트의 `*_BackUpThisFolder_ButDontShipItWithYourGame`, `*_BurstDebugInformation_DoNotShip`는 게시물에서 뺀다.

게시 폴더 배치는 `{product}/channels/{채널}.json`, `{product}/releases/{releaseId}/manifest.json`, `{product}/blobs/{sha 앞 2자}/{sha}`다. 지금은 로컬 폴더에서 읽고, 나중에 이 폴더를 NAS에 그대로 올린다. 외부 체험자가 받으려면 NAS를 HTTPS로 열어야 한다(공개 방식 미정).
