#!/usr/bin/env bash
# shinshi PostgreSQL 백업 (pg_dump 사용자 지정 형식, -Fc).
#
# 초기안(결정 전): 일 1회, 7일 보존, 다른 저장소에도 복사. 이 스크립트는 "이 호스트에 덤프를 만들고 오래된 것을 지우는 일"만 한다.
# 다른 저장소로의 복사는 하지 않는다(보관 위치 미결정, README.md 참고).
# 이 스크립트는 Linux 대상 환경에서 실행한 적이 없다. 개발 PC의 일회용 PostgreSQL 18 컨테이너에서만 시험했다.
#
# 사용법: backup.sh <출력 폴더> [보존 일수=7]
#
# 연결 정보는 libpq 환경변수로만 받는다: PGHOST, PGPORT, PGUSER, PGDATABASE(필수), PGSSLMODE 등.
# 비밀번호는 인자로 받지 않는다. ~/.pgpass(또는 PGPASSFILE) 또는 실행 환경의 PGPASSWORD로만 공급한다.
# 비밀번호를 물어보지 않으므로(--no-password) 비밀번호가 없으면 즉시 실패한다.
#
# 종료 코드: 0 성공, 1 사용법·환경 오류 또는 백업 실패(새 파일을 남기지 않는다),
#            2 백업은 성공했으나 오래된 파일 정리에 실패.

set -euo pipefail
umask 077

readonly PROGRAM="${0##*/}"
readonly DEFAULT_RETENTION_DAYS=7

fail() {
    printf '%s: 오류: %s\n' "$PROGRAM" "$*" >&2
    exit 1
}

usage() {
    cat >&2 <<EOF
사용법: $PROGRAM <출력 폴더> [보존 일수=$DEFAULT_RETENTION_DAYS]
  PGDATABASE(필수), PGHOST, PGPORT, PGUSER 등은 환경변수로 준다. 비밀번호는 .pgpass 또는 PGPASSWORD로만 준다.
  보존 일수 N: 만든 지 N일(N*24시간)을 넘은 이 DB의 백업 파일을 지운다.
EOF
    exit 1
}

[[ $# -ge 1 && $# -le 2 ]] || usage
out_dir=$1
retention_days=${2:-$DEFAULT_RETENTION_DAYS}

[[ -n $out_dir ]] || usage
[[ $retention_days =~ ^[1-9][0-9]{0,3}$ ]] || fail "보존 일수는 1~9999 사이의 정수여야 합니다: $retention_days"
[[ -n ${PGDATABASE:-} ]] || fail "PGDATABASE 환경변수가 필요합니다."
[[ $PGDATABASE =~ ^[A-Za-z0-9_.-]+$ ]] || fail "PGDATABASE에 파일 이름으로 쓸 수 없는 문자가 있습니다."

for tool in pg_dump pg_restore; do
    command -v "$tool" >/dev/null 2>&1 || fail "$tool 을 찾을 수 없습니다. 서버와 같은 주 버전(18) 이상의 PostgreSQL 클라이언트를 설치하세요."
done

mkdir -p -- "$out_dir" || fail "출력 폴더를 만들 수 없습니다: $out_dir"
[[ -d $out_dir && -w $out_dir ]] || fail "출력 폴더에 쓸 수 없습니다: $out_dir"

stamp=$(date -u +%Y%m%dT%H%M%SZ)
base="shinshi-${PGDATABASE}-${stamp}.dump"
final="$out_dir/$base"

# 같은 DB의 백업이 겹쳐 돌지 않게 배타 잠금을 잡는다(다른 DB 이름과는 서로 막지 않는다)
if command -v flock >/dev/null 2>&1; then
    exec 9>"$out_dir/.lock-${PGDATABASE}"
    flock -n 9 || fail "같은 DB의 다른 백업이 실행 중입니다."
else
    fail "flock 을 찾을 수 없습니다(util-linux). 동시 실행을 막을 수 없어 중단합니다."
fi

[[ ! -e $final ]] || fail "같은 이름의 파일이 이미 있습니다: $final"

# 실행마다 고유한 임시 파일. 같은 초에 시작해도 서로 덮어쓰지 않는다
partial=$(mktemp "$out_dir/.${base}.XXXXXX.partial") || fail "임시 파일을 만들 수 없습니다."

cleanup() {
    rm -f -- "$partial"
}
trap cleanup EXIT

# 같은 폴더의 임시 파일에 먼저 쓴다. 중간에 실패해도 완성된 백업처럼 보이는 파일이 남지 않는다
if ! pg_dump --no-password --format=custom --file="$partial"; then
    fail "pg_dump 가 실패했습니다. 연결 정보, 권한(모든 테이블 SELECT), 비밀번호 공급(.pgpass/PGPASSWORD)을 확인하세요."
fi

[[ -s $partial ]] || fail "백업 파일이 비어 있습니다."

# 목차를 읽을 수 있는지만 확인한다. 복원 가능성의 증명은 아니며 복원 리허설(restore.sh)이 따로 필요하다
if ! pg_restore --list "$partial" >/dev/null; then
    fail "만든 백업을 pg_restore 가 읽지 못했습니다."
fi

if command -v sha256sum >/dev/null 2>&1; then
    digest=$(sha256sum -- "$partial" | cut -d' ' -f1)
    printf '%s  %s\n' "$digest" "$base" >"$final.sha256"
fi

mv -- "$partial" "$final"
size=$(wc -c <"$final" | tr -d ' ')
printf '%s: 백업 완료: %s (%s bytes)\n' "$PROGRAM" "$final" "$size"

# 새 백업이 성공한 뒤에만 오래된 파일을 지운다. 이 DB 이름의 백업과 체크섬 파일만 대상이다
db_regex=${PGDATABASE//./\\.}
if ! find "$out_dir" -maxdepth 1 -type f -regextype posix-extended \
    -regex ".*/shinshi-${db_regex}-[0-9]{8}T[0-9]{6}Z\\.dump(\\.sha256)?" \
    -mmin "+$((retention_days * 1440))" -print -delete; then
    printf '%s: 경고: 오래된 백업 정리에 실패했습니다. 백업 자체는 만들어졌습니다.\n' "$PROGRAM" >&2
    exit 2
fi
