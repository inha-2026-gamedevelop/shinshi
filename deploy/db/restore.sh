#!/usr/bin/env bash
# shinshi PostgreSQL 복원 (pg_restore). 비어 있는 대상 DB에만 복원한다.
#
# 이 스크립트는 Linux 대상 환경에서 실행한 적이 없다. 개발 PC의 일회용 PostgreSQL 18 컨테이너에서만 시험했다.
#
# 사용법: restore.sh <덤프 파일>
#
# 대상 DB와 연결은 libpq 환경변수로만 준다: PGDATABASE(필수), PGHOST, PGPORT, PGUSER 등.
# 비밀번호는 .pgpass(PGPASSFILE) 또는 PGPASSWORD 로만 준다. 비밀번호를 물어보지 않는다(--no-password).
# 대상 DB는 미리 만들어 두어야 하고(CREATE DATABASE), 이 스크립트는 DB를 만들거나 지우지 않는다.
#
# 안전장치
#   1) 대상 DB에 사용자 객체(테이블, 뷰, 시퀀스 등)가 하나라도 있으면 거절한다. 덮어쓰기·병합은 하지 않는다.
#   2) 대상 DB 이름을 직접 입력해 확인한다. 비대화 실행은 RESTORE_CONFIRM_DB 가 PGDATABASE 와 같을 때만 통과한다.
#   3) --single-transaction 으로 복원한다. 중간에 오류가 나면 전부 취소되어 DB가 비어 있는 채로 남는다.
#
# 소유자와 권한
#   기본은 덤프에 든 소유자·GRANT 를 그대로 복원한다. 덤프 때의 역할(예: shinshi_migrator, shinshi_runtime)이 대상 서버에
#   없으면 pg_restore 가 실패한다. 역할을 먼저 만들거나(Backend/README.md 의 권한 SQL 참고), RESTORE_NO_OWNER=1 로
#   소유자·권한 복원을 건너뛴다. 이때 런타임 역할 권한을 Backend/README.md 의 SQL 로 다시 부여해야 API 가 동작한다(미검증 경로).
#
# 종료 코드: 0 성공, 1 사용법·사전 점검 거절, 2 복원 실패, 3 복원은 끝났으나 확인 SQL 실패.

set -euo pipefail
umask 077

readonly PROGRAM="${0##*/}"
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly script_dir

fail() {
    printf '%s: 오류: %s\n' "$PROGRAM" "$*" >&2
    exit 1
}

[[ $# -eq 1 ]] || {
    printf '사용법: %s <덤프 파일>\n  PGDATABASE(필수)와 PGHOST, PGPORT, PGUSER 는 환경변수로 준다. 대상 DB는 비어 있어야 한다.\n' "$PROGRAM" >&2
    exit 1
}
dump_file=$1

[[ -f $dump_file && -r $dump_file ]] || fail "덤프 파일을 읽을 수 없습니다: $dump_file"
[[ -n ${PGDATABASE:-} ]] || fail "PGDATABASE 환경변수가 필요합니다. 복원할 대상 DB 이름을 명시하세요."

for tool in pg_restore psql; do
    command -v "$tool" >/dev/null 2>&1 || fail "$tool 을 찾을 수 없습니다. 서버와 같은 주 버전(18) 이상의 PostgreSQL 클라이언트를 설치하세요."
done

# 체크섬 파일이 곁에 있으면 먼저 대조한다
if [[ -f $dump_file.sha256 ]] && command -v sha256sum >/dev/null 2>&1; then
    expected=$(cut -d' ' -f1 <"$dump_file.sha256")
    actual=$(sha256sum -- "$dump_file" | cut -d' ' -f1)
    [[ $expected == "$actual" ]] || fail "체크섬이 일치하지 않습니다. 덤프 파일이 손상됐을 수 있습니다: $dump_file"
    printf '%s: 체크섬 일치\n' "$PROGRAM"
fi

pg_restore --list "$dump_file" >/dev/null || fail "pg_restore 가 덤프 파일을 읽지 못했습니다(손상되었거나 다른 형식): $dump_file"

# 대상 DB 연결과 비어 있는지 확인
if ! existing=$(psql --no-psqlrc --no-password --tuples-only --no-align --quiet --set ON_ERROR_STOP=1 --command \
    "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname !~ '^pg_(toast|temp)'
        AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')"); then
    fail "대상 DB에 연결하지 못했습니다. PGHOST/PGPORT/PGUSER/PGDATABASE 와 비밀번호 공급(.pgpass/PGPASSWORD), DB 존재 여부를 확인하세요."
fi
existing=${existing//[[:space:]]/}
[[ $existing =~ ^[0-9]+$ ]] || fail "대상 DB 상태를 읽지 못했습니다."
if [[ $existing != 0 ]]; then
    fail "대상 DB '$PGDATABASE' 가 비어 있지 않습니다(사용자 객체 ${existing}개). 덮어쓰지 않습니다. 새 빈 DB를 만들어 다시 실행하세요."
fi

target="${PGHOST:-<기본 소켓>}:${PGPORT:-5432} / ${PGDATABASE}"
printf '%s: 복원 대상: %s\n%s: 덤프 파일: %s\n' "$PROGRAM" "$target" "$PROGRAM" "$dump_file"
if [[ ${RESTORE_NO_OWNER:-} == 1 ]]; then
    printf '%s: RESTORE_NO_OWNER=1 소유자와 권한은 복원하지 않습니다. 런타임 역할 권한을 직접 다시 부여해야 합니다.\n' "$PROGRAM"
fi

if [[ ${RESTORE_CONFIRM_DB:-} == "$PGDATABASE" ]]; then
    printf '%s: RESTORE_CONFIRM_DB 로 확인을 건너뜁니다.\n' "$PROGRAM"
elif [[ -t 0 ]]; then
    printf '이 DB에 복원합니다. 계속하려면 대상 DB 이름 [%s] 을 그대로 입력하세요: ' "$PGDATABASE" >&2
    read -r answer
    [[ $answer == "$PGDATABASE" ]] || fail "입력이 대상 DB 이름과 다릅니다. 취소했습니다."
else
    fail "확인할 수 없습니다. 대화형 터미널에서 실행하거나 RESTORE_CONFIRM_DB 를 대상 DB 이름과 같게 설정하세요."
fi

restore_args=(--no-password --exit-on-error --single-transaction --dbname="$PGDATABASE")
if [[ ${RESTORE_NO_OWNER:-} == 1 ]]; then
    restore_args+=(--no-owner --no-acl)
fi

if ! pg_restore "${restore_args[@]}" "$dump_file"; then
    printf '%s: 복원에 실패했습니다. 단일 트랜잭션이라 대상 DB는 비어 있어야 합니다. 위 pg_restore 메시지(역할 없음, 버전 차이 등)를 확인하세요.\n' "$PROGRAM" >&2
    exit 2
fi
printf '%s: 복원 완료. 확인 SQL 을 실행합니다.\n' "$PROGRAM"

if ! psql --no-psqlrc --no-password --set ON_ERROR_STOP=1 --file="$script_dir/verify-restore.sql"; then
    printf '%s: 복원은 끝났으나 확인 SQL 이 실패했습니다. 수동으로 확인하세요.\n' "$PROGRAM" >&2
    exit 3
fi

cat <<'EOF'

다음 확인 (deploy/runbook/data-and-release.md "DB 복원" 참고)
  1. API 의 외부 진입점을 연 채로 두지 말고, 복원본에 API 를 연결한다.
     service_state 는 백업 시점 값이 복원됐다. 점검이 꺼져 있었다면 검증 전에 플레이어가 로그인할 수 있다.
  2. API 시작 뒤 일일 대사 결과를 확인한다: ERROR 로그 "Economy ledger mismatch", economy_reconciliation_runs 의 mismatch_count.
  3. 백업 직전에 센 행 수와 위 결과를 비교한다.
  4. 운영자 로그인(shinshi-admin login)이 되는지 확인한다. Admin__TotpKeyBase64 가 백업 때와 같아야 로그인된다.
EOF
