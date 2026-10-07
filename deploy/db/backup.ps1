<#
.SYNOPSIS
shinshi PostgreSQL 백업 (Windows용, pg_dump -Fc). 로컬 리허설과 Windows 개발 PC용이다.
.DESCRIPTION
backup.sh 와 같은 동작이다: 날짜 파일명으로 덤프를 만들고, 성공한 뒤에만 보존 일수를 넘은 이 DB의 백업을 지운다.
초기안(결정 전): 일 1회, 7일 보존, 다른 저장소에도 복사. 이 스크립트는 다른 저장소로 복사하지 않는다.
이 스크립트는 개발 PC의 일회용 PostgreSQL 18 컨테이너에서만 시험했다. 운영 대상 환경에서 실행한 적이 없다.

두 가지 방식이 있다.
  1) 기본: PATH 의 pg_dump 를 쓴다. 연결은 PGHOST, PGPORT, PGUSER, PGDATABASE(필수) 환경변수로 준다.
     비밀번호는 인자로 받지 않는다. %APPDATA%\postgresql\pgpass.conf 또는 PGPASSWORD 환경변수로만 공급한다.
  2) -Container <이름>: Docker 컨테이너 안의 pg_dump 를 쓴다(tools/Start-ReleaseStack.ps1 의 컨테이너 shinshi-release-db).
     PGUSER, PGDATABASE 환경변수가 없으면 사용자 shinshi, DB shinshi 를 쓴다(그 스크립트의 값).
     공식 postgres 이미지의 컨테이너 안 로컬 소켓 접속을 쓰므로 비밀번호가 필요 없다.
종료 코드: 0 성공, 1 오류 또는 백업 실패, 2 백업은 성공했으나 오래된 파일 정리 실패.
.EXAMPLE
$env:PGDATABASE = 'shinshi'; .\deploy\db\backup.ps1 -OutDir D:\ShinshiBackup -RetentionDays 7
.EXAMPLE
.\deploy\db\backup.ps1 -OutDir D:\ShinshiBackup -Container shinshi-release-db
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [string] $RetentionDays = '7',
    [string] $Container = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string] $Message) {
    [Console]::Error.WriteLine("backup.ps1: 오류: $Message")
    exit 1
}

$days = 0
if (($RetentionDays -notmatch '\A[1-9][0-9]{0,3}\z') -or (-not [int]::TryParse($RetentionDays, [ref] $days))) {
    Fail "보존 일수는 1~9999 사이의 정수여야 합니다: $RetentionDays"
}

$containerMode = -not [string]::IsNullOrEmpty($Container)
$database = $env:PGDATABASE
$user = $env:PGUSER
if ($containerMode) {
    if ([string]::IsNullOrEmpty($database)) { $database = 'shinshi' }
    if ([string]::IsNullOrEmpty($user)) { $user = 'shinshi' }
    if ($null -eq (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'docker 를 찾을 수 없습니다.' }
}
else {
    if ([string]::IsNullOrEmpty($database)) { Fail 'PGDATABASE 환경변수가 필요합니다.' }
    foreach ($tool in @('pg_dump', 'pg_restore')) {
        if ($null -eq (Get-Command $tool -ErrorAction SilentlyContinue)) {
            Fail "$tool 을 찾을 수 없습니다. 서버와 같은 주 버전(18) 이상의 PostgreSQL 클라이언트를 설치하세요."
        }
    }
}
if ($database -notmatch '\A[A-Za-z0-9_.-]+\z') { Fail 'PGDATABASE 에 파일 이름으로 쓸 수 없는 문자가 있습니다.' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 같은 DB의 백업이 겹쳐 돌지 않게 파일 잠금을 잡고, 같은 PowerShell 세션에서 다시 실행해도 막히지 않게 끝에서 직접 푼다
try {
    $lockStream = [IO.File]::Open((Join-Path $OutDir ".lock-$database"), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
}
catch { Fail "같은 DB의 다른 백업이 실행 중입니다." }
try {
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$baseName = "shinshi-$database-$stamp.dump"
$final = Join-Path $OutDir $baseName
$runId = [Guid]::NewGuid().ToString("N")
$partial = Join-Path $OutDir ".$baseName.$runId.partial"
if (Test-Path -LiteralPath $final) { Fail "같은 이름의 파일이 이미 있습니다: $final" }

try {
    if ($containerMode) {
        $inside = "/tmp/$runId-$baseName"
        & docker exec $Container pg_dump -U $user -d $database --format=custom --file=$inside
        if ($LASTEXITCODE -ne 0) { Fail 'pg_dump 가 실패했습니다. 컨테이너 이름, 사용자, DB 이름을 확인하세요.' }
        & docker cp "${Container}:$inside" $partial
        $copyExit = $LASTEXITCODE
        & docker exec $Container rm -f $inside | Out-Null
        if ($copyExit -ne 0) { Fail '컨테이너에서 덤프를 꺼내지 못했습니다.' }
    }
    else {
        & pg_dump --no-password --format=custom --file=$partial
        if ($LASTEXITCODE -ne 0) {
            Fail 'pg_dump 가 실패했습니다. 연결 정보, 권한(모든 테이블 SELECT), 비밀번호 공급(pgpass.conf/PGPASSWORD)을 확인하세요.'
        }
    }

    if ((-not (Test-Path -LiteralPath $partial)) -or ((Get-Item -LiteralPath $partial).Length -eq 0)) {
        Fail '백업 파일이 비어 있습니다.'
    }

    if ($containerMode) {
        # 목차 확인은 컨테이너의 pg_restore 로 한다
        & docker cp $partial "${Container}:/tmp/check-$runId-$baseName"
        & docker exec $Container pg_restore --list "/tmp/check-$runId-$baseName" | Out-Null
        $listExit = $LASTEXITCODE
        & docker exec $Container rm -f "/tmp/check-$runId-$baseName" | Out-Null
        if ($listExit -ne 0) { Fail '만든 백업을 pg_restore 가 읽지 못했습니다.' }
    }
    else {
        & pg_restore --list $partial | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail '만든 백업을 pg_restore 가 읽지 못했습니다.' }
    }

    $hash = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$final.sha256", "$hash  $baseName`n", (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $partial -Destination $final
}
finally {
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue }
}

Write-Output ("backup.ps1: 백업 완료: {0} ({1} bytes)" -f $final, (Get-Item -LiteralPath $final).Length)

# 새 백업이 성공한 뒤에만, 이 DB 이름의 백업과 체크섬 파일만 지운다
$cutoff = (Get-Date).AddDays(-$days)
$nameRegex = '\Ashinshi-' + [regex]::Escape($database) + '-\d{8}T\d{6}Z\.dump(\.sha256)?\z'
try {
    Get-ChildItem -LiteralPath $OutDir -File |
        Where-Object { $_.Name -cmatch $nameRegex } |
        Where-Object { $_.LastWriteTime -lt $cutoff } |
        ForEach-Object { Write-Output $_.FullName; Remove-Item -LiteralPath $_.FullName -Force }
}
catch {
    [Console]::Error.WriteLine('backup.ps1: 경고: 오래된 백업 정리에 실패했습니다. 백업 자체는 만들어졌습니다.')
    exit 2
}
}
finally {
    $lockStream.Dispose()
}
