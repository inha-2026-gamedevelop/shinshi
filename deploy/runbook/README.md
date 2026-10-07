# 운영 런북

체험판 운영자가 사고와 정기 작업에서 따라 할 절차다. 절차마다 언제, 사전 확인, 명령, 확인 방법, 되돌리기를 적는다. 명령은 저장소에서 존재를 확인한 것만 쓰고, 확인하지 못한 것은 "미확인"으로 적는다.

> **이 런북의 어떤 절차도 실제로 실행한 적이 없다.** 2026-10-07 작업 트리의 코드와 문서를 읽어 쓴 정적 작성본이다. 명령 이름과 옵션은 `Backend/src/Shinshi.AdminCli`와 systemd 유닛 파일에서 확인했지만 실제 환경에서의 동작, 소요 시간, 출력은 검증하지 않았다. 리허설([rehearsal.md](rehearsal.md))을 마치고 결과를 기록하기 전까지는 "구현됨·실행 미검증" 절차로 취급한다. DB 백업·복구 목표(주기, 보존, 보관 위치, RPO/RTO)와 롤백 호환 조건은 사용자 결정 전이다.

## 문서

| 파일 | 내용 |
|---|---|
| [service-control.md](service-control.md) | 점검 시작·해제, 기능 차단, 버전 차단, 공지 작성·종료 |
| [incident-response.md](incident-response.md) | 경제 사고 1차 대응, 장애 세션 복구, 보정 지급, 개인 지급·회수, 강제 종료 |
| [servers-and-alerts.md](servers-and-alerts.md) | 서버 재시작·드레인, 경보별 대응 |
| [data-and-release.md](data-and-release.md) | DB 백업·복원, 빌드 롤백, 비밀값 보관 |
| [rehearsal.md](rehearsal.md) | 리허설 (a) 로컬 CLI·API, (b) Linux 대상 환경 시험 체크리스트, 결과 기록 양식 |
| [PLAYER_BUG_REPORT.md](PLAYER_BUG_REPORT.md) | 외부 체험판 플레이어용 버그 제보 안내 |
| [../db/README.md](../db/README.md) | 백업·복원 스크립트와 systemd timer 예시 |

## 도구 지도

업무 변경과 서버 프로세스, DB는 각각 다른 도구가 맡는다. 다른 도구의 일을 흉내 내지 않는다.

| 하려는 일 | 도구 | 비고 |
|---|---|---|
| 점검, 기능 차단, 허용 빌드, 공지, 지급·조정, 강제 종료(토큰 폐기), 장애 세션 복구, 조회 | `shinshi-admin`(어드민 API만 호출, DB 직접 접속 없음) | 모든 쓰기는 감사 로그에 남는다 |
| 게임 서버(마을, 던전) 시작·중지·재시작, 로그 | `systemctl`, `journalctl`([deploy/gameserver](../gameserver/README.md)) | 서버 재시작·드레인을 하는 `shinshi-admin` 명령은 없다 |
| API 컨테이너 교체 | Docker([deploy/README.md](../README.md)) | 컨테이너 이름·배포 방식은 배포 환경 결정 전 |
| DB 백업·복원 | `pg_dump`/`pg_restore`([deploy/db](../db/README.md)) | `shinshi-admin`에 백업·복원 명령은 없다 |
| 마이그레이션 | API 이미지의 `--migrate`(인자 하나) | 시작 때 자동 실행하지 않는다 |
| 클라이언트 배포·롤백 | `Shinshi.Release.Packager`(`keygen`, `publish`)와 허용 빌드 목록 | 롤백 전용 명령은 없다 |

## 시작 전 준비

- `shinshi-admin`은 빌드 결과물 이름이다(AssemblyName). 빌드된 실행 파일이 없으면 저장소 루트에서 `dotnet run --project Backend/src/Shinshi.AdminCli -- <명령>`으로 같은 일을 한다. 아래는 `shinshi-admin`으로 줄여 쓴다. 전체 명령·옵션은 `shinshi-admin --help`, `shinshi-admin <명령> --help`와 [AdminCli README](../../Backend/src/Shinshi.AdminCli/README.md)다.
- API 주소는 `--api <주소>`, 없으면 환경변수 `SHINSHI_ADMIN_API`, 없으면 `http://127.0.0.1:5080`이다.
- 어드민 경로는 서버 설정 `Admin__AllowedNetworks__*` 안의 출발지에서만 열린다. 밖에서는 본문 없는 404이고 도구는 `--api` 주소와 접속 위치를 확인하라고 안내한다.
- 로그인: `shinshi-admin login --user <이름>`. 비밀번호와 TOTP는 인자로 받지 않고 콘솔에서 에코 없이 묻는다. TOTP 코드는 30초마다 바뀌고 같은 코드는 한 번만 받으므로 로그인 실패 시 다음 코드를 기다린다. 같은 운영자가 5회 연속 실패하면 15분 잠긴다. 토큰은 30분(기본)이며 만료되면 도구가 세션 파일을 지우고 종료 코드 3으로 끝난다.
- 역할은 Viewer < Operator < Admin < Owner다. 점검·기능 차단·허용 빌드 저장(`service-state set`, `allow-build`)과 세션 복구(`recover`)는 Admin, 지급·조정·공지·강제 종료는 Operator, 조회는 Viewer다. 대상 2명 이상의 지급 실행과 `RecoveryCompensation` 지급 실행은 Admin이다. 운영자 생성·비활성화는 Owner다.
- 쓰기는 모두 요약을 보이고 `계속할까요? (y/N)`를 묻는다. `n`이면 서버로 보내지 않고(종료 코드 1) operationId도 만들지 않는다. `--yes`는 확인만 건너뛴다.
- `--reason`은 감사 로그에 그대로 남는다. 비밀번호·초대 코드·토큰을 적지 않는다. 계정 상세, 원장, 아이템 이력, 세션 이벤트, 작업 조회도 사유가 필수이고 읽기 전에 감사 행이 남는다.

## 쓰기 재시도 원칙 (AdminCli 코드로 확인)

**업무 변경은 출력된 같은 operationId, 같은 본문, 같은 대상으로 재전송할 때만 중복 효과가 막힌다. CLI를 그냥 다시 실행하면 새 ID가 생겨 같은 작업이 한 번 더 실행될 수 있다.**

- 쓰기 명령은 확인 질문에 `y`한 직후 새 operationId(GUID)를 만들어 `operationId: ...`로 출력한다(`--json`이면 표준 오류로). 이 값을 사고 기록에 적어 둔다.
- 응답을 못 받았을 때(연결 실패, 시간 초과, 503, 429, 202 PENDING. 종료 코드 3)는 처리됐는지 알 수 없다. 같은 명령을 **인자 전부(사유 포함, 대상 포함) 그대로** 두고 `--operation-id <출력된 ID>`만 붙여 다시 실행한다. 서버가 처음 확정한 결과를 돌려주고 효과는 다시 일어나지 않는다.
- 같은 ID에 다른 내용(사유가 다르거나 대상이 다름)을 보내면 409 `IDEMPOTENCY_CONFLICT`다. 효과는 없다. 새 작업에는 `--operation-id`를 쓰지 않는다.
- 종료 코드 2(API 거절)는 같은 요청을 다시 보내도 같은 결과다. 재전송하지 말고 원인(대상 없음, 상태 불일치, 권한 등)을 고친 뒤 **새 ID로 새 명령**을 실행한다. 일시 실패한 최초 요청도 ID와 본문 결속은 남으므로, 다른 내용의 작업은 반드시 새 ID를 쓴다.
- `service-state set`은 지정하지 않은 항목을 현재 값으로 채워 보내므로, 응답을 잃은 뒤에 같은 명령에 ID만 붙이면 그 사이 바뀐 값이 섞여 본문이 달라진다. 도구가 출력하는 "현재 값을 다시 읽지 않는 재시도 명령"(네 값 `--maintenance`, `--disable`, `--allow-builds`, `--expected-revision`과 `--operation-id`, `--reason`을 모두 담음)을 그대로 쓴다. 그 명령은 PowerShell 7.3 이상에서만 따옴표가 보존된다. 도구가 "재시도 명령으로 옮길 수 없다"고 경고한 경우(쉼표가 든 값 등)에는 `service-state show`로 결과를 확인한다.
- 터미널 기록을 잃어 ID를 모르면: `shinshi-admin audit --admin <내 adminId> --from <ISO8601 UTC 시각>`(`audit`은 사유가 필요 없다)으로 확정된 쓰기의 `operationId`, 사유, 대상을 볼 수 있다. 내 adminId는 감사 표의 행위자 열(`Admin:<adminId>`)이나 `operators list`(Owner)에서 찾는다. 감사에 없다고 해서 "서버에 닿지 않았다"는 증거는 아니다(확정되기 전에 끊겼을 수 있다). 해당 상태를 조회 명령으로 직접 확인한 뒤에만 새 ID로 다시 실행한다.
- 재시도 명령은 같은 운영자로 로그인한 상태에서 보낸다. 작업 키는 (운영자, operationId)다.

### 한 번만 나오는 값은 재전송으로 복구되지 않는다

- 초대 코드 원문(`invites issue`), 운영자 초기 비밀번호와 TOTP 비밀(`operators create`)은 서버가 그 생성을 처음 확정한 응답에만 있다. 도구는 화면에 한 번만 출력하고 저장하지 않는다. 같은 operationId 재전송 응답은 같은 inviteId·adminId를 주지만 원문·비밀값은 없다(null).
- 잃었으면: 초대는 `invites revoke <inviteId>` 후 새로 발급한다. 운영자는 `operators disable <adminId>` 후 다시 만든다. 비활성화한 운영자의 로그인 이름을 다시 쓸 수 있는지는 미확인이다(행을 지우지 않으므로 409 `LOGIN_NAME_TAKEN`일 수 있다. 그러면 다른 이름을 쓴다).
- `--json` 출력에는 위 값이 그대로 들어간다. 파이프와 파일에 넘길 때 주의한다.

## 종료 코드

| 코드 | 뜻 | 다음 행동 |
|---|---|---|
| 0 | 성공 | 확인 방법으로 결과 확인 |
| 1 | 사용법 오류, 쓰기 확인 거절 | 서버로 안 보냄. 인자를 고쳐 다시 |
| 2 | API 거절(400, 403, 404, 409 등) | 원인 해결 후 새 ID. 같은 요청은 같은 결과 |
| 3 | 연결 실패, 시간 초과, 로그인 필요, 세션 만료(401), 503, 429, 202 | 로그인 문제면 다시 로그인. 쓰기였다면 같은 ID로 재전송 |

## 절차 색인

| 절차 | 위치 |
|---|---|
| 점검 시작, 점검 해제 | [service-control.md](service-control.md#점검-시작) |
| 기능 차단·해제 | [service-control.md](service-control.md#기능-차단) |
| 버전 차단(허용 빌드) | [service-control.md](service-control.md#버전-차단) |
| 공지 작성·종료 | [service-control.md](service-control.md#공지-작성과-종료) |
| 경제 사고 1차 대응(경매 중복 지급 의심) | [incident-response.md](incident-response.md#경제-사고-1차-대응-경매-중복-지급-의심) |
| 장애 세션 복구(RetrySettlement, ServiceCancel, ForceClose) | [incident-response.md](incident-response.md#장애-세션-복구) |
| 보정 지급 | [incident-response.md](incident-response.md#보정-지급) |
| 개인 지급·회수 | [incident-response.md](incident-response.md#개인-지급과-회수) |
| 강제 종료(kick)와 한계 | [incident-response.md](incident-response.md#강제-종료) |
| 서버 재시작·드레인 | [servers-and-alerts.md](servers-and-alerts.md#서버-재시작과-드레인) |
| 경보 대응 | [servers-and-alerts.md](servers-and-alerts.md#경보-대응) |
| DB 백업·복원 | [data-and-release.md](data-and-release.md#db-백업) |
| 빌드 롤백 | [data-and-release.md](data-and-release.md#빌드-롤백) |
| 비밀값 보관 | [data-and-release.md](data-and-release.md#비밀값-보관) |
| 리허설과 결과 기록 | [rehearsal.md](rehearsal.md) |

## 근거 문서

[운영 도구·로그 설계](../../docs/design/operations-admin.md), [Backend README 운영자 어드민 기반(0008)](../../Backend/README.md), [AdminCli README](../../Backend/src/Shinshi.AdminCli/README.md), [게임 서버 유닛](../gameserver/README.md), [배포 기반](../README.md), [모니터링](../monitoring/README.md), [로그 규약](../LOGGING.md), [기술 설계 15장](../../docs/03-technical-design.md).
