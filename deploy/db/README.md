# DB 백업·복원 스크립트

PostgreSQL 덤프(`pg_dump -Fc`)를 만들고 복원하는 스크립트와 systemd timer 예시다. 운영 절차와 판단 기준은 [운영 런북](../runbook/data-and-release.md)에 있다.

**상태: 초기안, 결정 전.** [기술 설계 15.2](../../docs/03-technical-design.md)의 "DB 백업·복구 목표"는 아직 정해지지 않았다. 아래 값은 체험판용 초기안이며 사용자 결정 뒤 바꾼다. 스크립트는 Linux 대상 환경이나 실제 RDS에서 실행한 적이 없다(시험 범위는 맨 아래).

| 항목 | 초기안 |
|---|---|
| 주기 | 하루 1회 (`shinshi-db-backup.timer`, 시각 미정) |
| 보존 | 7일 (`backup.sh` 두 번째 인자) |
| 보관 위치 | 백업 호스트 외 다른 저장소에도 복사(방법·위치 미결정, 이 스크립트는 복사하지 않음) |
| 허용 데이터 손실(RPO)·복구 시간(RTO) | 미결정. 일 1회 덤프면 RPO는 최대 약 하루다 |
| PITR(연속 보관) | 미결정. 관리형 DB의 자동 백업을 쓸지도 미결정 |

## 파일

| 파일 | 용도 |
|---|---|
| [backup.sh](backup.sh) | 덤프 생성, 목차 검증, SHA-256 파일, 보존 일수 초과분 삭제 |
| [restore.sh](restore.sh) | 빈 DB에만 복원, 이름 입력 확인, 단일 트랜잭션, 끝에 [verify-restore.sql](verify-restore.sql) 실행 |
| [verify-restore.sql](verify-restore.sql) | 복원 직후 읽기 전용 확인(마이그레이션 목록, 서비스 상태, 행 수, 일일 대사 기록) |
| [backup.ps1](backup.ps1), [restore.ps1](restore.ps1) | Windows용. `-Container`로 Docker 컨테이너 안의 도구를 쓸 수 있다(로컬 스택 `shinshi-release-db`) |
| [systemd/](systemd) | `shinshi-db-backup.service`, `.timer`, `backup.env.example` |
| [.gitattributes](.gitattributes) | `.sh`, `.service`, `.timer`가 Windows 체크아웃에서도 LF를 유지하게 한다 |

## 사용

연결 정보는 환경변수(`PGHOST`, `PGPORT`, `PGUSER`, `PGDATABASE`, `PGSSLMODE`)로만 준다. 비밀번호는 인자로 받지 않으며 `.pgpass`(`PGPASSFILE`)나 실행 환경의 `PGPASSWORD`로만 공급한다. 두 스크립트 모두 비밀번호를 묻지 않고(`--no-password`) 없으면 실패한다.

```bash
# 백업: 출력 폴더, 보존 일수(기본 7)
PGDATABASE=shinshi ./backup.sh /var/backups/shinshi 7

# 복원: 비어 있는 대상 DB를 먼저 만들어 둔다. 대상 DB 이름을 입력해 확인한다
PGDATABASE=shinshi_restore ./restore.sh /var/backups/shinshi/shinshi-shinshi-20261007T033000Z.dump
```

- 파일 이름은 `shinshi-<DB 이름>-<UTC 시각>.dump`이고 곁에 `.sha256`이 생긴다. 보존 일수 N은 "만든 지 N일(N×24시간)을 넘은 같은 DB 이름의 파일 삭제"다. 새 백업이 성공한 뒤에만 지운다.
- `backup.sh`는 같은 폴더의 `.partial` 파일에 먼저 쓰고 성공하면 이름을 바꾼다. 실패하면 완성본처럼 보이는 파일을 남기지 않는다. 종료 코드는 0 성공, 1 실패, 2 백업은 됐으나 정리 실패다.
- `restore.sh`는 대상 DB에 사용자 객체가 하나라도 있으면 거절한다. 종료 코드는 0 성공, 1 사전 점검 거절, 2 복원 실패, 3 복원은 됐으나 확인 SQL 실패다. 비대화 실행은 `RESTORE_CONFIRM_DB=<대상 DB 이름>`이 필요하다.
- 소유자·권한: 기본은 덤프의 소유자와 GRANT를 그대로 복원하므로 덤프 때의 역할(`shinshi_migrator`, `shinshi_runtime` 등)이 대상 서버에 있어야 한다. 없으면 `RESTORE_NO_OWNER=1`로 건너뛸 수 있으나 그러면 [Backend/README.md](../../Backend/README.md)의 권한 SQL을 다시 적용해야 API가 동작한다(그 경로는 시험하지 않았다).
- 백업 역할은 모든 테이블을 읽어야 한다. 런타임 역할은 `schema_migrations` 읽기 권한이 없으므로 쓰지 않는다. 전용 읽기 역할을 정하는 일은 미결정이다.
- 클라이언트 도구(`pg_dump`, `pg_restore`, `psql`)는 서버와 같은 주 버전(PostgreSQL 18) 이상이어야 한다.

## systemd timer

`systemd/` 파일을 `/etc/systemd/system/`에 설치하고 `systemctl daemon-reload` 뒤 `systemctl enable --now shinshi-db-backup.timer`로 켠다. 선행 조건(스크립트 위치, `backup.env`, `.pgpass` 권한, 사용자)은 유닛 파일 머리 주석에 있다. 실패 알림은 없다(Alertmanager 미연결). 다른 저장소 복사 단계는 `ExecStartPost`에 보관 위치가 정해진 뒤 추가한다.

## 시험 범위

실행한 것(2026-10-07, 개발 PC의 Docker `postgres:18` 일회용 컨테이너, 저장소의 0001~0013 `.up.sql`을 직접 적용한 빈 데이터):

- `bash -n`, `shellcheck`(koalaman/shellcheck:stable) 통과.
- `backup.sh`: 정상 백업, 잘못된 비밀번호·비밀번호 없음 실패(0 아닌 종료, 파일 안 남김), 잘못된 보존 일수·`PGDATABASE` 없음 거절, 보존 초과분만 삭제(다른 DB 이름과 무관한 파일은 유지).
- `restore.sh`: 비어 있지 않은 DB 거절, 확인 없는 비대화 실행 거절, 틀린 이름 입력 거절(의사 터미널), 손상된 덤프 거절, 빈 DB 복원과 확인 SQL, `RESTORE_NO_OWNER=1` 복원.
- `backup.ps1`, `restore.ps1`: `-Container` 방식으로 같은 시나리오 일부(정상, 거절, 복원).
- `systemd-analyze verify`로 `.service`, `.timer` 문법 확인(Ubuntu 24.04 컨테이너).

실행하지 못한 것: 실제 운영 데이터 규모, RDS, `.pgpass` 인증 경로, Windows의 PATH `pg_dump` 방식, timer의 실제 구동, 다른 저장소 복사, 역할이 없는 서버로의 복원 오류 경로, API와 연결한 복원 후 대사. 일회용 컨테이너에서는 `shinshi_migrator` 등 역할 소유권을 재현하지 않았다.
