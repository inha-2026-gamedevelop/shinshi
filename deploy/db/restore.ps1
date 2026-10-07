<#
.SYNOPSIS
shinshi PostgreSQL 복원 (Windows용, pg_restore). 비어 있는 대상 DB에만 복원한다.
.DESCRIPTION
restore.sh 와 같은 안전장치를 쓴다: 대상 DB에 사용자 객체가 있으면 거절, 대상 DB 이름 직접 입력 확인(또는 RESTORE_CONFIRM_DB),
단일 트랜잭션 복원. DB 를 만들거나 지우지 않으므로 빈 DB 를 먼저 만들어 둔다.
이 스크립트는 개발 PC의 일회용 PostgreSQL 18 컨테이너에서만 시험했다. 운영 대상 환경에서 실행한 적이 없다.

두 가지 방식은 backup.ps1 과 같다.
  1) 기본: PATH 의 pg_restore, psql. 연결은 PGHOST, PGPORT, PGUSER, PGDATABASE(필수), 비밀번호는 pgpass.conf 또는 PGPASSWORD.
  2) -Container <이름>: 컨테이너 안의 도구를 쓴다. PGUSER, PGDATABASE 가 없으면 shinshi, shinshi.
소유자·권한: 기본은 덤프의 것을 그대로 복원한다. 역할이 없으면 실패한다. RESTORE_NO_OWNER=1 환경변수면 건너뛴다(권한을 다시 부여해야 함).
종료 코드: 0 성공, 1 사전 점검 거절, 2 복원 실패, 3 복원은 끝났으나 확인 SQL 실패.
.EXAMPLE
$env:PGDATABASE = 'shinshi_restore'; .\deploy\db\restore.ps1 D:\ShinshiBackup\shinshi-shinshi-20261007T000000Z.dump
.EXAMPLE
$env:PGDATABASE = 'shinshi_restore'; .\deploy\db\restore.ps1 D:\ShinshiBackup\x.dump -Container shinshi-release-db
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)] [string] $DumpFile,
    [string] $Container = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string] $Message, [int] $Code = 1) {
    [Console]::Error.WriteLine("restore.ps1: 오류: $Message")
    exit $Code
}

if (-not (Test-Path -LiteralPath $DumpFile -PathType Leaf)) { Fail "덤프 파일을 읽을 수 없습니다: $DumpFile" }
$DumpFile = (Resolve-Path -LiteralPath $DumpFile).Path

$containerMode = -not [string]::IsNullOrEmpty($Container)
$database = $env:PGDATABASE
$user = $env:PGUSER
if ($containerMode) {
    if ([string]::IsNullOrEmpty($database)) { $database = 'shinshi' }
    if ([string]::IsNullOrEmpty($user)) { $user = 'shinshi' }
    if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'docker 를 찾을 수 없습니다.' }
}
else {
    if ([string]::IsNullOrEmpty($database)) { Fail 'PGDATABASE 환경변수가 필요합니다. 복원할 대상 DB 이름을 명시하세요.' }
    foreach ($tool in @('pg_restore', 'psql')) {
        if ($null -eq (Get-Command $tool -ErrorAction SilentlyContinue)) {
            Fail "$tool 을 찾을 수 없습니다. 서버와 같은 주 버전(18) 이상의 PostgreSQL 클라이언트를 설치하세요."
        }
    }
}

$checksumPath = "$DumpFile.sha256"
if (Test-Path -LiteralPath $checksumPath -PathType Leaf) {
    $expected = ((Get-Content -LiteralPath $checksumPath -TotalCount 1) -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $DumpFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) { Fail "체크섬이 일치하지 않습니다. 덤프 파일이 손상됐을 수 있습니다: $DumpFile" }
    Write-Output 'restore.ps1: 체크섬 일치'
}

$emptySql = "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname !~ '^pg_(toast|temp)' AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')"
$insideDump = "/tmp/restore-$([Guid]::NewGuid().ToString('N')).dump"

function Invoke-Psql([string[]] $Arguments) {
    if ($containerMode) { & docker exec $Container psql -U $user -d $database --no-psqlrc @Arguments }
    else { & psql --no-psqlrc --no-password @Arguments }
}

try {
    if ($containerMode) {
        & docker cp $DumpFile "${Container}:$insideDump"
        if ($LASTEXITCODE -ne 0) { Fail '덤프를 컨테이너로 복사하지 못했습니다. 컨테이너 이름을 확인하세요.' }
        & docker exec $Container pg_restore --list $insideDump | Out-Null
    }
    else {
        & pg_restore --list $DumpFile | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { Fail "pg_restore 가 덤프 파일을 읽지 못했습니다(손상되었거나 다른 형식): $DumpFile" }

    $existing = (Invoke-Psql @('--tuples-only', '--no-align', '--quiet', '--set', 'ON_ERROR_STOP=1', '--command', $emptySql) | Out-String).Trim()
    if (($LASTEXITCODE -ne 0) -or ($existing -notmatch '\A[0-9]+\z')) {
        Fail '대상 DB에 연결하지 못했습니다. PGHOST/PGPORT/PGUSER/PGDATABASE, 비밀번호 공급, DB 존재 여부를 확인하세요.'
    }
    if ($existing -ne '0') {
        Fail "대상 DB '$database' 가 비어 있지 않습니다(사용자 객체 ${existing}개). 덮어쓰지 않습니다. 새 빈 DB를 만들어 다시 실행하세요."
    }

    $hostText = if ($containerMode) { "컨테이너 $Container" } elseif ($env:PGHOST) { "$($env:PGHOST):$(if ($env:PGPORT) { $env:PGPORT } else { '5432' })" } else { '<기본 접속>' }
    Write-Output "restore.ps1: 복원 대상: $hostText / $database"
    Write-Output "restore.ps1: 덤프 파일: $DumpFile"
    $noOwner = ($env:RESTORE_NO_OWNER -eq '1')
    if ($noOwner) { Write-Output 'restore.ps1: RESTORE_NO_OWNER=1 소유자와 권한은 복원하지 않습니다. 런타임 역할 권한을 직접 다시 부여해야 합니다.' }

    if ($env:RESTORE_CONFIRM_DB -ceq $database) {
        Write-Output 'restore.ps1: RESTORE_CONFIRM_DB 로 확인을 건너뜁니다.'
    }
    elseif ([Environment]::UserInteractive -and (-not [Console]::IsInputRedirected)) {
        $answer = Read-Host "이 DB에 복원합니다. 계속하려면 대상 DB 이름 [$database] 을 그대로 입력하세요"
        if ($answer -cne $database) { Fail '입력이 대상 DB 이름과 다릅니다. 취소했습니다.' }
    }
    else {
        Fail '확인할 수 없습니다. 대화형 터미널에서 실행하거나 RESTORE_CONFIRM_DB 를 대상 DB 이름과 같게 설정하세요.'
    }

    $restoreArgs = @('--exit-on-error', '--single-transaction')
    if ($noOwner) { $restoreArgs += @('--no-owner', '--no-acl') }
    if ($containerMode) { & docker exec $Container pg_restore -U $user @restoreArgs --dbname=$database $insideDump }
    else { & pg_restore --no-password @restoreArgs --dbname=$database $DumpFile }
    if ($LASTEXITCODE -ne 0) {
        Fail '복원에 실패했습니다. 단일 트랜잭션이라 대상 DB는 비어 있어야 합니다. 위 pg_restore 메시지(역할 없음, 버전 차이 등)를 확인하세요.' 2
    }
    Write-Output 'restore.ps1: 복원 완료. 확인 SQL 을 실행합니다.'

    $verifyPath = Join-Path $PSScriptRoot 'verify-restore.sql'
    if ($containerMode) {
        & docker cp $verifyPath "${Container}:/tmp/verify-restore.sql"
        Invoke-Psql @('--set', 'ON_ERROR_STOP=1', '--file=/tmp/verify-restore.sql')
        $verifyExit = $LASTEXITCODE
        & docker exec $Container rm -f /tmp/verify-restore.sql | Out-Null
    }
    else {
        Invoke-Psql @('--set', 'ON_ERROR_STOP=1', "--file=$verifyPath")
        $verifyExit = $LASTEXITCODE
    }
    if ($verifyExit -ne 0) { Fail '복원은 끝났으나 확인 SQL 이 실패했습니다. 수동으로 확인하세요.' 3 }
}
finally {
    if ($containerMode) { & docker exec $Container rm -f $insideDump | Out-Null }
}

Write-Output ''
Write-Output '다음 확인 (deploy/runbook/data-and-release.md "DB 복원" 참고)'
Write-Output '  1. 복원본에 API 를 연결하기 전에 외부 진입점을 닫아 둔다. service_state 는 백업 시점 값이 복원됐다.'
Write-Output '  2. API 시작 뒤 일일 대사 결과를 확인한다: ERROR 로그 "Economy ledger mismatch", economy_reconciliation_runs 의 mismatch_count.'
Write-Output '  3. 백업 직전에 센 행 수와 위 결과를 비교한다.'
Write-Output '  4. shinshi-admin login 이 되는지 확인한다. Admin__TotpKeyBase64 가 백업 때와 같아야 한다.'
