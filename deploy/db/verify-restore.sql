-- 복원 직후 읽기 전용 확인. restore.sh 가 끝에 실행하고, 수동으로도 실행할 수 있다.
--   psql -X -v ON_ERROR_STOP=1 -f deploy/db/verify-restore.sql
-- 연결은 PG* 환경변수를 따른다. 이 파일은 아무것도 바꾸지 않는다.
-- 이 결과는 "스키마와 행이 돌아왔는가"만 보여 준다. 원장 대사 일치는 API의 일일 대사가 판정한다(deploy/runbook/data-and-release.md).

\echo == 적용된 마이그레이션 (Backend/src/Shinshi.Data/Migrations 의 최신 번호까지 있어야 한다)
SELECT version, name, applied_at FROM schema_migrations ORDER BY version;

\echo == 서비스 상태 (복원본에는 백업 시점의 점검·차단·허용 빌드가 그대로 들어 있다)
SELECT maintenance, disabled_features, allowed_build_ids, revision, updated_at FROM service_state;

\echo == 주요 테이블 행 수 (백업 직전에 같은 쿼리로 센 값과 비교한다)
SELECT 'accounts' AS table_name, count(*) AS row_count FROM accounts
UNION ALL SELECT 'characters', count(*) FROM characters
UNION ALL SELECT 'operations', count(*) FROM operations
UNION ALL SELECT 'economy_ledger', count(*) FROM economy_ledger
UNION ALL SELECT 'game_sessions', count(*) FROM game_sessions
UNION ALL SELECT 'admin_users', count(*) FROM admin_users
UNION ALL SELECT 'admin_audit_log', count(*) FROM admin_audit_log
ORDER BY table_name;

\echo == 일일 대사 기록 (오늘 UTC 날짜 행이 이미 끝난 상태로 있으면 API 가 오늘은 다시 돌리지 않는다)
SELECT run_date, started_at, finished_at, mismatch_count FROM economy_reconciliation_runs ORDER BY run_date DESC LIMIT 5;

\echo == 끝나지 않은 세션 (복원 시점에 Running/Settling/Auction 이던 출정. 서버 프로세스는 이미 없을 수 있다)
SELECT state, count(*) AS sessions FROM game_sessions GROUP BY state ORDER BY state;
