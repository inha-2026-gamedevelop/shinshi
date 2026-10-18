# 리허설

**아직 한 번도 실행하지 않았다.** 이 문서는 런북을 검증하기 위한 절차와 기록 양식이다. 리허설을 마치고 아래 기록 양식을 채우기 전까지 런북의 모든 절차는 "구현됨·실행 미검증"이다.

리허설은 둘로 나뉜다. (a)는 Windows 개발 PC에서 CLI와 API 조작을 익히는 것이고, (b)는 Linux 대상 환경에서 종료 신호, 던전 정산·장애, 백업 복원, 롤백을 시험하는 것이다. **(a)는 (b)를 대신하지 못한다.** 아래 제약이 이유다.

## (a) 로컬 CLI·API 조작 리허설

### 스택 선택

| 방법 | 필요한 것 | 비고 |
|---|---|---|
| `tools/Start-ReleaseStack.ps1` | Windows, Docker Desktop, .NET 10 SDK, `Builds/windows-server`(클라이언트까지 쓰려면 `Builds/windows-client`) | 마을 서버, 던전 서버(감시 루프가 출정마다 새로 띄움), 개발 클라이언트까지 띄운다. Unity 빌드가 없으면 `-Build`(Unity 에디터를 닫아야 함, 수 분 소요) |
| 백엔드 단독([Backend/README.md](../../Backend/README.md)의 로컬 실행) | Docker, .NET 10 SDK | Unity 빌드 불필요. DB와 API만 있어 클라이언트가 필요한 시험(재접속, kick 뒤 접속 유지, 우편 수령)은 못 한다 |

### Start-ReleaseStack.ps1 제약 (스크립트에서 확인)

1. **Windows 전용**이다(`$env:OS`가 `Windows_NT`가 아니면 중단). Linux 서버, systemd, 종료 신호 시험이 아니다.
2. **시작할 때마다 점검과 허용 빌드를 덮어쓴다.** 마이그레이션 뒤 `service_state`에 직접 SQL로 `maintenance = false`, `allowed_build_ids = {dev-local, 서버 빌드의 buildId, (클라이언트가 있으면) 클라이언트 buildId}`를 쓴다. 감사 로그를 거치지 않는다. 기능 차단 목록(`disabled_features`)은 건드리지 않는다. 그래서 리허설 중에 스크립트를 다시 실행하면 점검 켜기와 허용 빌드 변경이 사라진다. `-Stop`은 DB를 지우지 않으므로 이전 차단 상태가 남을 수 있다.
3. API는 `ASPNETCORE_ENVIRONMENT=Development`, HTTP(TLS·프록시 없음), `ServiceState__Maintenance=false`로 뜬다. 운영 설정(Production, `Admin__AllowedNetworks__*` 지정, 프록시 뒤 IP 판정)은 시험되지 않는다. `Admin__AllowedNetworks`가 없으므로 어드민은 같은 PC의 루프백(`http://127.0.0.1:5080`, CLI 기본값과 같음)에서만 열린다.
4. **운영자(Owner)를 만들지 않는다.** 아래 준비 단계에서 `--bootstrap-owner`를 직접 실행한다.
5. DB 컨테이너(`postgres:18`)는 사용자 `shinshi` 하나가 전부 소유한다. 마이그레이션 역할과 런타임 역할의 분리, 권한 SQL은 시험되지 않는다.
6. 마을 서버 하나와 던전 서버 `-DungeonCount`개(기본 4, 최대 8, 포트 `-DungeonPort`(기본 7780)부터)가 뜬다. 던전은 출정 1회가 끝나면 스스로 종료하므로 `tools/Watch-ReleaseDungeons.ps1`(숨김 백그라운드 프로세스)이 같은 인스턴스 ID로 새 프로세스를 몇 초 안에 다시 띄운다. 던전 슬롯 하나는 출정 하나만 맡으므로 슬롯 수가 곧 동시에 출정할 수 있는 파티 수다. 슬롯이 모두 차면 다음 파티는 마을 출정 대기열(NO_CAPACITY, 5초마다 재예약)에서 기다리고 클라이언트에는 대기 상태가 보인다. 두 파티 이상을 동시에 시험하려면 `-DungeonCount`를 파티 수 이상으로 둔다. 출정 시험을 하지 않으면 진행 중 세션이 없고 `sessions`, `auctions list`는 비어 있다. **장애 세션 복구(`recover`), 경매, 서버 소실 판정은 실제 데이터로 시험할 수 없다.** `recover`는 없는 세션 ID로 거절 경로만 볼 수 있다.
7. `-Stop`은 감시 루프를 먼저 끄고 던전 프로세스까지 `taskkill /T /F`로 프로세스 트리를 강제 종료한다. SIGTERM 처리, 드레인, 종료 제한 시간 시험이 아니다.
8. 클라이언트는 개발 빌드와 개발 로그인(아이디·비밀번호·이메일 가입)이다. 릴리스 클라이언트는 런처 세션만 받으므로 런처 흐름은 시험되지 않는다.
9. Prometheus, Grafana, Alertmanager는 이 스크립트가 시작하지 않는다. 경보 시험이 아니다.
10. 비밀값(DB 비밀번호, 인증 키, 어드민 TOTP 키, 서비스 토큰)은 `%LOCALAPPDATA%\Shinshi\release-stack\`에 파일로 생긴다. 화면에 나오지 않으며 저장소 밖이다. 채팅·문서에 옮기지 않는다. `-Stop -RemoveDatabase`는 컨테이너와 `db_password`만 지우고 다른 비밀 파일은 남긴다.

### 준비

```powershell
.\tools\Start-ReleaseStack.ps1 -ClientCount 0          # 클라이언트 없이 API·DB·마을 서버만
# 스크립트 출력의 로그 위치를 기록한다. 아래는 로컬 DB(기본 포트 55440)에 최초 Owner를 만드는 예다.
$state = Join-Path $env:LOCALAPPDATA 'Shinshi\release-stack'
$dbPassword = (Get-Content (Join-Path $state 'db_password') -Raw).Trim()
$env:ConnectionStrings__Postgres = "Host=127.0.0.1;Port=55440;Username=shinshi;Password=$dbPassword;Database=shinshi"
$env:Admin__TotpKeyBase64 = (Get-Content (Join-Path $state 'admin_totp_key') -Raw).Trim()
$env:SHINSHI_BOOTSTRAP_OWNER = 'owner.rehearsal'
dotnet run --project Backend/src/Shinshi.Api -- --bootstrap-owner
```

`--bootstrap-owner`가 표준 출력에 한 번만 쓰는 비밀번호와 TOTP 비밀(otpauth URI)로 인증 앱을 등록하고, 끝나면 환경변수(`ConnectionStrings__Postgres`, `Admin__TotpKeyBase64`, `SHINSHI_BOOTSTRAP_OWNER`)를 지운다. 이 명령은 `Backend/README.md`의 설명만으로 쓴 것이며 실행해 확인하지 않았다(미검증). 클라이언트가 필요한 시험은 `-ClientCount 1`로 시작한다.

### 시나리오

각 줄은 런북 절차 하나를 한 번 수행하고 기대 결과를 확인한다. 쓰기는 모두 `operationId`를 기록한다. 표의 명령은 줄여 쓴 것이다. **모든 쓰기 명령에는 `--reason "<사유>"`가 필수**이고(없으면 종료 코드 1), `adjust`는 `--asset`과 `--delta`, `notices create`는 `--title --body --target --severity --ends`, `invites issue`는 `--max-uses --hours`, `recover`는 `--action`이 필수다. PowerShell에서 `@파일` 값은 작은따옴표로 감싼다.

| ID | 절차(런북) | 해 볼 것 | 기대 |
|---|---|---|---|
| A1 | 로그인, 조회 | `shinshi-admin login --user owner.rehearsal`, `service-state show`, `notices list`, `sessions`, `auctions list`, `audit` | 점검 꺼짐, 허용 빌드에 `dev-local`, 목록 비어 있음 |
| A2 | 한 번만 나오는 값 | Owner로 `operators create --user ops.rehearsal --role operator --reason ...`. 출력된 초기 비밀번호·TOTP 비밀로 두 번째 운영자 로그인. 같은 명령에 `--operation-id`를 붙여 재전송 | 첫 응답에만 비밀값. 재전송은 같은 adminId에 "비밀값이 응답에 없습니다" 경고 |
| A3 | 초대 | `invites issue --max-uses 1 --hours 1 --reason ...`, 이어서 `--operation-id`로 재전송 | 원문은 첫 응답에만. 재전송에는 없음 |
| A4 | 점검 시작·해제 | `service-state set --maintenance on`, 공개 `Invoke-RestMethod http://127.0.0.1:5080/v1/service-state`, 해제 | 값 반영. 클라이언트 로그인이 점검으로 거절되는지(클라이언트 있을 때) |
| A5 | 재시도 원칙 | A4의 출력된 재시도 명령을 그대로 다시 실행. 이어서 같은 `--operation-id`에 다른 `--reason`으로 실행 | 첫째는 처음 결과, 둘째는 `IDEMPOTENCY_CONFLICT`(종료 코드 2). CLI를 ID 없이 다시 실행하면 새 ID가 생기는 것도 확인 |
| A6 | 기능 차단 | `--disable inbox_claim`을 먼저 적용한 뒤 `--disable auction`만 적용(통째 교체로 `inbox_claim`이 풀리는지), 해제 `--disable none` | 교체 동작과 `show` 출력 확인 |
| A7 | 버전 차단 | `allow-build add rehearsal-b2`, 다시 add(이미 있음), `remove rehearsal-b2` | 두 번째는 "이미 허용 목록에 있어 저장하지 않습니다"(종료 코드 0) |
| A8 | 공지 | `notices create --target Both --severity info --ends <+1시간 UTC>`, `list`, 공개 응답의 `notices`, `end` | 노출 후 종료. 런처 표시는 런처가 있을 때만 |
| A9 | 개인 지급·회수 | `adjust <accountId> --asset Currency --delta 500`, `--delta -100000`(가용량 초과), `ledger --account ... --reason ...` | 지급 확정, 초과 회수는 `shortfall` 표시, 원장에 `ADMIN_GRANT`/`ADMIN_REVOKE` |
| A10 | 보정 지급·권한 | Operator(`ops.rehearsal`)로 대상 2명 `grants create` 후 `execute`, Owner(Admin 이상)로 `execute`, `show`, 다른 지급은 `cancel` | Operator의 실행은 403 `FORBIDDEN_ROLE`, Owner는 성공 |
| A11 | 강제 종료 한계 | (클라이언트 필요) 접속 중인 계정에 `accounts kick`, 접속 유지 여부와 같은 비밀번호 재로그인 | 출력 `현재 접속 유지`, 재로그인 가능(= 문서가 말한 한계) |
| A12 | 감사와 ID 복구 | 터미널 기록이 없다고 치고 `audit --admin <adminId> --from <시각>`으로 직전 쓰기의 `operationId` 찾기 | 감사 표에 `operationId`와 사유 |
| A13 | 복구 거절 경로 | `recover <임의 GUID> --action service-cancel --reason ...` | `NOT_FOUND`(종료 코드 2). 실제 복구는 (b)에서 |
| A14 | 로컬 백업·복원 | `.\deploy\db\backup.ps1 -OutDir <폴더> -Container shinshi-release-db`, 빈 DB를 `CREATE DATABASE`로 만들고 `$env:PGDATABASE='shinshi_restore'; $env:RESTORE_CONFIRM_DB='shinshi_restore'; .\deploy\db\restore.ps1 <덤프> -Container shinshi-release-db` | 확인 SQL에서 마이그레이션 이력과 행 수가 백업 전과 같음. 현재 전체 스키마는 0016까지이며 2026-10-07의 0013 백업 시험과 구분 |
| A15 | 복원본으로 API 연결(선택) | 위 복원 DB의 연결 문자열로 API를 따로 실행(`ASPNETCORE_URLS`는 다른 포트), 일일 대사 로그와 `shinshi-admin login`(`--api` 다른 포트) | 대사 `0 mismatches`, 같은 `Admin__TotpKeyBase64`로 로그인 성공. 다른 키로는 실패(확인) |
| A16 | 문서 대조 | 위를 하면서 런북의 명령과 출력이 다른 곳을 기록 | 문서 수정 항목 |

(a)가 끝나도 **확인되지 않는 것**: Linux 신호 처리, 던전 세션 복구·경매, 허용 네트워크·프록시 뒤 동작, 실제 RDS, 경보 전달, 타이머 구동, 롤백.

## (b) Linux 대상 환경 시험 체크리스트

대상 환경(서버 한두 대와 PostgreSQL)이 준비된 뒤 수행한다. 아래 명령은 런북이 쓰는 것과 같다. 기대 결과를 모르는 항목은 "관찰"로 적었다. 관찰한 값은 런북의 해당 절차에 반영한다(예: 서버 소실 판정까지의 시간, 정상 종료 소요).

| ID | 영역 | 시험 | 방법(명령) | 합격 기준·관찰 |
|---|---|---|---|---|
| B1 | 환경 | 유닛 설치와 비루트 실행 | `sudo systemd-analyze verify /etc/systemd/system/shinshi-town.service /etc/systemd/system/shinshi-dungeon@.service`, `systemctl enable --now`, `ss -lunp` | `ProtectSystem=strict` 아래에서 서버가 뜨고 포트가 열림 |
| B2 | 종료 신호 | 유휴 마을 중지 | `sudo systemctl stop shinshi-town.service`, `journalctl -u shinshi-town.service` | 서버가 SIGTERM을 받아 종료 처리를 하는지, 걸린 시간(300초 상한 대비) 관찰 |
| B3 | 종료 신호 | 던전 정상 종료 | 출정 하나를 끝낸 뒤 종료 코드와 재기동 | 종료 코드 0, 새 bootId로 재기동, 서버 풀에 다시 등록 |
| B4 | 종료 신호 | 진행 중 세션 있는 서버 중지 | 세션 진행 중 `systemctl stop`, SIGKILL 여부 | 세션이 서버 소실로 보이는지, 정상 정산으로 오기록되지 않는지 |
| B5 | 던전 장애 | 서버 소실 → 장애 취소 | 진행 중 `sudo kill -9 <던전 PID>`, `shinshi-admin sessions` 반복 | `서버 소실=예`가 되기까지 시간 기록. `recover <id> --action service-cancel` 성공, 티켓·물약 환급·정기 보상 원장 확인(`operation`, `ledger`) |
| B6 | 던전 장애 | 생존 판정 보류 | API 재시작 직후 30초 안에 `recover --action service-cancel` | 503 `BACKEND_UNAVAILABLE`, 종료 코드 3, 같은 ID로 재시도 성공 |
| B7 | 정산 장애 | Settling 정체 → `retry-settlement` | API를 정산 시점에 잠시 중지해 Settling을 만들고 복구 | 같은 정산이 한 번만 적용 |
| B8 | 정산 장애 | 격리 → `force-close` | 격리(복구 실패 5회) 재현 방법은 **미확인**. 개발자와 재현 방법을 정한다 | 장애 취소 보상으로 닫힘, 원래 보상과의 차이 확인 |
| B9 | 저장 큐 | API 중지 시 큐 정체 | API 중지, `ShinshiStorageQueueStuck` 대기, API 복구 | 큐가 줄어드는지, 이벤트 유실 없는지 |
| B10 | 재시작 | 시작 제한 | 일부러 틀린 `CONTENT_VERSION`으로 시작 → 300초 5회 제한 → 원인 수정 → `reset-failed`, `start` | 문서의 절차대로 복구 |
| B11 | 경보 | 전달 경로 | Alertmanager 연결 뒤 지표 포트를 막는 등으로 시험 경보 1건. 각 신규 API 경보를 한 번씩 발화(예: API 컨테이너 중지) | 팀 채널에서 실제로 받음 |
| B12 | 백업 | 실제 DB 백업 | `deploy/db/backup.sh`, `systemctl start shinshi-db-backup.service`, `list-timers` | 파일·체크섬 생성, timer 구동, 보존 삭제 |
| B13 | 복원 | 새 DB 복원과 검증 | `restore.sh`(역할 있는 서버), 실패 경로(역할 없음)도 한 번 | 확인 SQL, 일일 대사 0, 백업 시점 `Admin__TotpKeyBase64`로 운영자 로그인. **소요 시간(RTO 후보)과 데이터 손실(RPO)을 기록** |
| B14 | 복원 | 복원 중 진입 차단 | 복원본의 `service_state`가 점검 꺼짐일 때 외부 진입점을 닫은 채 검증 | 검증 전에 플레이어가 못 들어옴 |
| B15 | 롤백 | 마이그레이션 없는 배포 롤백 | 이전 릴리스로 `current`·`CONTENT_VERSION`·API 이미지 되돌림, `allow-build add/remove` | 이전 빌드로 접속·출정 가능 |
| B16 | 롤백 | 마이그레이션 포함 배포의 호환 | 마지막 마이그레이션 직전의 API 이미지로 현재 스키마에서 시작·로그인·출정 | 호환 여부를 마이그레이션별로 기록(롤백 정책 결정 자료) |
| B17 | 접근 | 허용 네트워크와 프록시 | 허용 밖 출발지에서 `login` → 404, 프록시 뒤 IP 판정(`ForwardedHeaders__KnownProxies__*`) | 허용 안에서만 열림 |
| B18 | 로그 | 정산 하나 추적 | journald와 API 로그에서 세션 ID, operationId로 정산 하나를 처음부터 끝까지 추적 | 로그만으로 추적 가능 |
| B19 | 마을 | 마을 재시작 영향 | 접속자가 있는 상태에서 `systemctl restart shinshi-town.service` | 재접속 동작, 미확정 요청 처리 관찰 |
| B20 | 시간 | 점검 전 과정 | 점검 시작부터 서버 교체, 해제까지 시간 측정 | 소요 시간과 막힌 지점 기록 |

## 기록 양식

리허설마다 아래를 채운다. 비밀값(비밀번호, TOTP 비밀, 키, 토큰, 초대 코드 원문)은 적지 않는다. 팀에 필요한 결과 요약(날짜, 실행 조건, 한계)은 해당 런북 문서에 반영하고, 원시 출력과 개인 메모는 Git에서 제외된 개인 기록 영역에 둔다([문서 정책](../../docs/DOCUMENTATION_POLICY.md)). 과거 성공 기록은 그 당시 빌드의 결과이며 현재 코드의 새 검증이 아니다.

```markdown
## 리허설 기록 <YYYY-MM-DD>

- 종류: (a) 로컬 / (b) Linux 대상
- 수행자 / 참관자:
- 환경: OS, 저장소 커밋, 서버 빌드 buildId, API 이미지 digest, PostgreSQL 버전, shinshi-admin 빌드
- 사용한 런북 커밋:
- 시작·종료 시각(UTC):

| ID | 절차 | 실행한 명령(비밀값 제외) | 기대 | 실제 | 종료 코드 | 소요 | 판정(PASS/FAIL/SKIP) | operationId·로그 위치 |
|---|---|---|---|---|---|---|---|---|

- 문서와 달랐던 점(문서 수정 항목):
- 건너뛴 항목과 이유:
- 발견한 결함, 사용자 결정이 필요한 것:
- 측정값(서버 소실 판정 시간, 종료 소요, 복원 소요, 데이터 손실 등):
- 후속 조치와 담당:
```

## 리허설 결과 요약

| 날짜 | 종류 | 범위 | 결과 | 한계 | 기록 위치 |
|---|---|---|---|---|---|
| (없음) | | | 아직 실행하지 않음 | | |
