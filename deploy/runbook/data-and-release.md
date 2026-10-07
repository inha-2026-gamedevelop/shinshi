# 데이터와 배포: DB 백업·복원, 빌드 롤백, 비밀값

스크립트는 [deploy/db](../db/README.md)에 있다. 실행한 적 없는 정적 작성본이며 **백업·복구 목표(주기, 보존, 보관 위치, RPO/RTO, PITR)와 롤백 호환 조건은 사용자 결정 전**이다. 아래 값은 초기안이다.

| 결정 항목 | 초기안(결정 전) | 상태 |
|---|---|---|
| 주기·보존 | 일 1회 덤프, 7일 보존 | 미결정 |
| 보관 위치 | 다른 저장소에도 복사 | 위치·방법 미결정 |
| 허용 데이터 손실(RPO) | 일 1회면 최대 약 하루 | 미결정 |
| 복구 시간(RTO) | 없음 | 미결정 |
| PITR(시점 복원) 또는 관리형 DB 자동 백업 | 없음 | 미결정 |
| 롤백 호환 조건 | 마이그레이션은 앞으로만 / 다운 마이그레이션 | 배포(U19) 때 결정 |

## DB 백업

**언제.** 정기(하루 1회 timer), 마이그레이션이 든 배포 직전, 대량 지급·복구 작업 직전.

**사전 확인.**

- 백업 호스트의 `pg_dump`/`pg_restore`가 서버와 같은 주 버전(18) 이상이다.
- 백업 역할이 모든 테이블을 읽을 수 있다. 런타임 역할(`shinshi_runtime`)은 `schema_migrations` 읽기 권한이 없으므로 쓰지 않는다. 전용 읽기 역할 구성은 미결정이다.
- 연결 정보는 `PG*` 환경변수, 비밀번호는 `.pgpass`(`PGPASSFILE`)나 `PGPASSWORD`로만 준다. 스크립트 인자와 이 문서에 비밀번호를 쓰지 않는다.
- 출력 폴더에 여유 공간이 있다.

**명령.**

```bash
# 수동 실행(Linux). 출력 폴더, 보존 일수
PGHOST=... PGUSER=... PGDATABASE=shinshi PGSSLMODE=require ./deploy/db/backup.sh /var/backups/shinshi 7

# timer로 돌릴 때(deploy/db/systemd 설치 뒤)
sudo systemctl list-timers shinshi-db-backup.timer
sudo systemctl start shinshi-db-backup.service        # 지금 한 번 실행
sudo journalctl -u shinshi-db-backup.service -n 50 --no-pager
```

```powershell
# Windows(개발 PC 또는 로컬 스택). 컨테이너 안의 pg_dump를 쓸 수도 있다
.\deploy\db\backup.ps1 -OutDir D:\ShinshiBackup -Container shinshi-release-db
```

백업 직전에 [verify-restore.sql](../db/verify-restore.sql)의 행 수 쿼리를 같은 DB에서 실행해 값을 기록해 두면 복원 뒤 비교할 수 있다.

**확인.**

- 종료 코드 0과 `백업 완료` 출력, 파일 크기가 0이 아님, 곁의 `.sha256`. 종료 코드 2는 백업은 됐으나 오래된 파일 정리에 실패한 것이다.
- 스크립트의 목차 검증(`pg_restore --list`)은 읽을 수 있다는 뜻일 뿐 **복원 가능의 증명이 아니다.** 주기적으로 복원 리허설을 한다([rehearsal.md](rehearsal.md)).
- timer 실패는 알림이 없다. `systemctl status shinshi-db-backup.service`나 `journalctl`로 직접 확인한다. 백업 파일 최신 시각이 24시간을 넘으면 실패로 본다.
- 다른 저장소 복사는 스크립트가 하지 않는다. 방법이 정해지기 전에는 같은 호스트의 디스크 장애를 못 견딘다.

**되돌리기.** 백업은 읽기만 한다. 보존 일수를 넘긴 파일 삭제는 되돌릴 수 없으니 보존 일수를 줄이기 전에 확인한다.

## DB 복원

**언제.** 데이터 손상, 잘못된 마이그레이션 뒤, 서버 장애 복구, 복원 리허설. 복원은 데이터를 백업 시점으로 되돌린다. 백업 이후의 계정 가입, 지급, 원장, 세션 결과가 사라지므로 플레이어에게 영향이 있다. 사용자 결정 없이 운영 DB를 복원하지 않는다.

**사전 확인.**

- 새 빈 DB에 복원한다. 스크립트는 사용자 객체가 있는 DB를 거절하고 DB를 만들거나 지우지 않는다. 기존 DB는 그대로 두면 되돌릴 길이 남는다.
- 대상 서버에 덤프의 소유자 역할(예: `shinshi_migrator`, `shinshi_runtime`)이 있다. 없으면 `RESTORE_NO_OWNER=1`로 소유자·권한 복원을 건너뛰고 [Backend/README.md](../../Backend/README.md)의 권한 SQL을 직접 다시 적용한다(그 경로는 시험하지 않았다).
- **복원 중 플레이어가 들어오지 못하게 한다.** 복원본의 `service_state`(점검, 차단, 허용 빌드)는 백업 시점 값이다. 점검이 꺼져 있던 시점의 백업이면 검증 전에 로그인이 열린다. API를 복원본에 연결하기 전에 외부 진입점(보안 그룹, 프록시)을 닫는다.
- 게임 서버(마을·던전)의 상태는 복원본과 맞지 않는다. 복원 전에 서버를 중지하고([servers-and-alerts.md](servers-and-alerts.md#계획된-교체재시작-마을-또는-던전-전체) 4단계) 복원 뒤 새로 시작한다. 복원 시점에 진행 중이던 세션을 서버가 재전송했을 때의 동작은 미확인이다.
- 복원본을 읽는 API의 `Admin__TotpKeyBase64`와 `Auth__HashKeyBase64`가 **백업 때와 같다.** 다르면 운영자 로그인 또는 토큰 대조가 실패한다([비밀값 보관](#비밀값-보관)).

**명령.**

```bash
# 대상 DB는 미리 CREATE DATABASE 로 만든 빈 DB
PGHOST=... PGUSER=... PGDATABASE=shinshi_restore ./deploy/db/restore.sh /var/backups/shinshi/shinshi-shinshi-<UTC 시각>.dump
```

대화형 터미널에서는 대상 DB 이름을 입력해 확인한다. 비대화 실행은 `RESTORE_CONFIRM_DB=<대상 DB 이름>`이 필요하다. 복원은 단일 트랜잭션이라 중간 실패 시 대상 DB는 비어 있다(종료 코드 2). 끝에 확인 SQL이 자동 실행된다(종료 코드 3은 복원은 됐으나 확인 SQL 실패).

**확인(복원 성공 기준: 대사 불일치 0, 로그인 가능).**

1. 확인 SQL 출력: `schema_migrations`가 저장소의 최신 번호(현재 0013)까지 있다. 행 수가 백업 직전 기록과 맞다. `game_sessions`에 끝나지 않은 상태가 남았다면 서버는 이미 없을 수 있다.
2. API를 복원 DB에 연결한다(런타임 연결 문자열 교체). 복원본 버전이 코드보다 낮으면 `--migrate`(마이그레이션 역할 연결)를 먼저 실행한다. 점검을 켠 채로 시작하려면 복원본이 이미 점검 상태여야 한다. 아니면 외부 진입점을 닫은 채 진행한다.
3. **대사.** 일일 대사는 API 시작 때 한 번, 이후 15분마다 "오늘(UTC) 실행을 선점"해 하루 한 번 돈다. 복원본에 오늘 날짜 행이 없으면 시작 직후 돌고 API 로그에 `Economy reconciliation for <날짜> finished with 0 mismatches.`가 나와야 한다. `Economy ledger mismatch` ERROR 로그와 경보 `ShinshiReconciliationMismatch`가 없어야 한다. 복원본에 이미 끝난 오늘 행이 있으면 그날은 다시 돌지 않는다. 그때는 DB 소유자 권한으로 오늘 행을 지우고 API를 재시작해 다시 돌릴 수 있다: `DELETE FROM economy_reconciliation_runs WHERE run_date = (clock_timestamp() AT TIME ZONE 'UTC')::date;`. 런타임 역할은 이 테이블에 DELETE 권한이 없다. 이 방법은 코드 읽기로만 확인했고 실행하지 않았다.
4. **운영자 로그인.** `shinshi-admin login --user <운영자>`가 되고 `service-state show`, `audit`이 읽힌다.
5. 플레이어 경로: 허용 빌드로 시험 계정 로그인, 마을 입장, 출정 한 번(진입점을 시험용으로만 열어서).
6. 모두 통과한 뒤에만 `service-state`의 점검 상태를 의도한 값으로 하고 외부 진입점을 연다.
7. 복원 시점 이후의 손실은 사고 기록에 적고 공지를 판단한다. 복원 시점에 진행 중이던 세션은 [장애 세션 복구](incident-response.md#장애-세션-복구)로 정리한다.

**되돌리기.** 원 DB를 건드리지 않았으므로 API 연결 문자열을 원 DB로 돌리면 된다. 복원 DB를 버리려면 DB 관리자가 직접 지운다(스크립트가 지우지 않는다).

## 빌드 롤백

롤백 전용 `shinshi-admin` 명령은 없다. 서버, 클라이언트, API, DB 스키마가 각각 다른 수단이다. 마이그레이션이 든 배포의 롤백 가능 여부는 아래 "스키마 호환"을 먼저 본다.

| 구성요소 | 롤백 수단 | 비고 |
|---|---|---|
| 게임 서버 | `/opt/shinshi/gameserver/current`가 가리키는 릴리스를 `releases/<이전 buildId>/`로 되돌리고 환경파일 `CONTENT_VERSION`을 이전 값으로 맞춘 뒤 서비스 시작 | 이전 릴리스 폴더를 지우지 않고 보관해야 가능. 링크 방식은 gameserver README 서술을 따른다 |
| API | 이전 이미지(태그·digest)로 컨테이너 교체 | 이전 이미지를 보관해야 한다. 운영 게시 전 digest 고정은 deploy README의 요구 사항 |
| 클라이언트 | 허용 빌드 목록과 런처 게시. `service-state allow-build add <이전 buildId>`, `remove <문제 buildId>` | 런처 게시 도구(`Shinshi.Release.Packager`)에는 `keygen`, `publish`만 있고 롤백 명령이 없다. 이전 빌드 폴더를 더 큰 `--sequence`로 다시 `publish`하는 방식이 가능해 보이나(sequence는 같은 채널의 이전 게시보다 커야 한다) 실행해 확인하지 않았다. 런처가 설치본보다 낮은 `buildId`를 받는지는 미확인 |
| DB 스키마 | 없음(앞으로만 적용). 아래 "스키마 호환" | 데이터 복원은 [DB 복원](#db-복원) |

### 스키마 호환 (사실대로)

- 마이그레이션 실행기(`SqlMigrationRunner`)는 번호순 SQL을 이력·체크섬과 같은 트랜잭션에 적용하고, 재실행은 건너뛰며, 이미 적용한 SQL의 변경은 거절한다. **`.down.sql`을 실행하는 기능은 없다.** `.down.sql`은 검토용이다. 시작 때 자동 마이그레이션도 없다.
- `0009`~`0013`의 `.down.sql`은 머리 주석이 데이터 손실을 경고한다(경매 기록·입찰 예약, 아이디·비밀번호 해시와 캐릭터 이름, 장비·성장·대사 기록, 회수함 미수령분 등을 잃고 일부는 "운영 DB에서 실행하지 않는다"고 적혀 있다). 운영 롤백 수단으로 쓸 수 없다.
- "이전 바이너리가 새 스키마에서 동작하는가"에 대한 **정책이 저장소에 없다.** 조사 기록의 결정 항목은 "배포(U19) 때 결정"이다. [deploy/README.md](../README.md)는 운영자가 "이전 API와 호환되는 스키마 변경인지 확인하고 마이그레이션 → API 교체 순서를 지킨다"고만 요구한다.
- 확인된 사례(코드 주석과 SQL):
  - `0010`은 `auth_token_families.origin`을 `NOT NULL`로 더한 뒤 기본값을 **일부러 지운다**. 0010 이전 바이너리가 새 로그인 묶음을 만들면 NOT NULL 위반으로 실패하도록 설계했다. 즉 0010 이전 API는 현재 스키마에서 동작하지 않는다.
  - `0012`는 `characters`에 삭제 표시 열을 더했다. 0012 `.down.sql` 주석은 되돌리면 삭제한 캐릭터가 다시 목록에 나타난다고 적고 있어, 이전 바이너리가 이 열을 모르는 경우 같은 현상이 생길 수 있다(이전 바이너리의 실제 동작은 미확인).
  - `0012`, `0013`은 `session_events` 종류 CHECK 범위를 넓혔다. 새 종류의 행이 이미 쌓인 DB에서 이전 코드가 그 행을 읽을 때의 동작은 미확인이다.
  - 나머지 마이그레이션별 이전 바이너리 호환은 검증하지 않았다.
- 결론: **마이그레이션이 없는(스키마가 같은) 배포의 롤백만 안전하다고 말할 수 있다.** 마이그레이션이 든 배포를 되돌리는 선택지는 (1) 배포 직전 백업으로 DB 복원(그 이후 데이터 손실), (2) 앞으로 고치기(fix-forward), (3) 이전 바이너리와 새 스키마의 호환을 그 마이그레이션마다 사전에 확인한 경우의 바이너리 롤백이다. 어느 정책으로 갈지는 사용자 결정이다.

### 배포 전 체크리스트(마이그레이션 포함 시)

1. 이전 서버 릴리스, API 이미지, 클라이언트 빌드 폴더가 보관돼 있다.
2. 직전 수동 백업을 만들었고 행 수를 기록했다.
3. 새 마이그레이션이 이전 바이너리와 호환되는지(열 추가는 기본값이 있는지, CHECK·NOT NULL 변경이 있는지) 확인해 기록했다.
4. 점검을 켰다.

### 롤백 절차

**언제.** 새 빌드에서 정산 오류, 서버 크래시 반복, 로그인 불가 같은 사고가 나고 수정 배포보다 되돌림이 빠를 때.

**사전 확인.** 위 "스키마 호환"으로 롤백이 가능한 배포인지 판단한다. 불가하면 (1) 또는 (2)를 사용자와 정한다.

**절차(마이그레이션 없는 배포 기준).**

1. 점검을 켜고 공지를 올린다([service-control.md](service-control.md)).
2. 진행 세션을 마감하고 서버를 중지한다([servers-and-alerts.md](servers-and-alerts.md#계획된-교체재시작-마을-또는-던전-전체) 2~4단계).
3. API를 이전 이미지로 교체한다. 이때 `--migrate`를 실행하지 않는다.
4. 서버의 `current`와 `CONTENT_VERSION`을 이전 값으로 되돌리고 시작한다.
5. `shinshi-admin service-state allow-build add <이전 buildId> --reason "롤백"`, 이어서 문제 빌드는 `remove`.
6. 클라이언트 롤백이 필요하면 위 표의 방식(미검증)을 따른다.
7. 확인 후 점검을 해제한다.

**확인.** 서버 `systemctl status`와 시작 로그, API `GET /v1/service-state`와 `/health/ready`, 시험 계정 로그인·마을 입장·출정, 경보 없음, 일일 대사 불일치 없음.

**되돌리기.** 새 빌드를 다시 배포한다(`current` 링크와 이미지 교체).

## 비밀값 보관

값은 문서, 로그, 채팅, 저장소에 쓰지 않는다. 아래는 **이름과 용도뿐**이다. 비밀 저장소 선택(관리형 비밀 저장소, 매개변수 저장소, 권한을 제한한 환경 파일)은 미결정이다([기술 설계 15.2](../../docs/03-technical-design.md)).

| 이름 | 용도 | 잃거나 바꾸면 |
|---|---|---|
| `Admin__TotpKeyBase64` | 운영자 TOTP 비밀을 AES-256-GCM으로 암호화하는 32바이트 무작위 키(Base64) | **운영자 인증 복구 불가.** DB에는 암호문만 있어 키 없이 TOTP를 풀 수 없다. 운영자 생성은 Owner 로그인이 필요하고 `--bootstrap-owner`는 `admin_users`가 비어 있을 때만 성공하므로 코드상 복구 경로가 없다. DB를 직접 고쳐 복구하는 방법의 영향(추가 전용 감사 로그와 운영자 FK)은 확인하지 않았다 |
| `Auth__HashKeyBase64` | 게임 토큰, 어드민 토큰, 초대 코드, 요청 IP의 HMAC 키 | 코드 설명상 바뀌면 기존 토큰과 초대 코드의 해시 대조가 달라져 무효가 될 것으로 보인다(추정, 미검증) |
| `ConnectionStrings__Postgres` | API 런타임 DML 전용 DB 연결 | API 기동 불가. 마이그레이션 연결을 대신 쓰지 않는다 |
| `ConnectionStrings__PostgresMigrator` | 마이그레이션 전용 연결(`--migrate`만) | 실행 중인 API에 공급하지 않는다 |
| 백업 역할 비밀번호(`.pgpass`/`PGPASSWORD`) | `pg_dump` 인증 | 백업 실패 |
| 최초 Owner 비밀번호·TOTP 비밀 | `--bootstrap-owner` 출력(1회) | 다시 볼 수 없다. 인증 앱과 비밀번호 보관소로 옮긴 뒤 터미널 기록을 지운다 |
| 서버 서비스 자격 | 게임 서버가 API에 인증 | 공급 방식·이름 미확정(gameserver README). 값을 실행 인자·URL·로그에 넣지 않는다 |
| 런처 게시 개인키 파일 | `Shinshi.Release.Packager publish`의 서명 | 파일은 저장소 밖에 둔다. 잃으면 새 키로 게시해야 하는 영향은 미확인 |

규칙:

- **DB 백업만으로는 운영자 로그인을 복원하지 못한다.** 복원본의 `admin_users`에는 암호문만 있다. `Admin__TotpKeyBase64`는 DB 백업과 **다른 곳**에 따로 보관한다. 키는 서로 독립된 두 곳 이상에 둔다.
- 키 값이 아니라 **보관 위치와 접근 가능한 사람**을 별도 기록(저장소 밖)에 둔다.
- 복원 리허설에서 백업 시점의 키로 운영자 로그인이 되는지 반드시 확인한다([rehearsal.md](rehearsal.md)).
- 키를 바꾸면 기존 운영자의 TOTP를 풀 수 없다. 키 로테이션 절차는 없고 지원 여부도 확인하지 않았다. 바꾸기 전에 Owner 복구 방법을 개발자와 정한다.
- 비밀이 로그나 채팅에 노출됐으면 노출된 값을 폐기하고 교체하는 것이 원칙이나, 각 값의 교체 절차는 위 표에 적은 영향 때문에 값마다 확인이 필요하다(미확인).
