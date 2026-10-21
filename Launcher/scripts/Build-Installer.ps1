param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [string]$CertificatePath,
    [string]$OutputDirectory = "$PSScriptRoot/../.local/installer"
)
$ErrorActionPreference = 'Stop'
$config = (Resolve-Path -LiteralPath $ConfigPath).Path
$document = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
if (-not $document.patchSource -or -not $document.publicKeys) { throw 'Patch source and public keys are required.' }
if ($CertificatePath -and -not $document.teamBuild) { throw 'A private CA is only supported for team builds.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path $output ('build-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
& dotnet publish "$PSScriptRoot/../src/Shinshi.Launcher.UI" -c Release -r win-x64 --self-contained true -o $payload `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
# The example configuration is for developers, not the installed product.
$example = Join-Path $payload 'launcher.example.json'
if (Test-Path -LiteralPath $example) { Remove-Item -LiteralPath $example }
Copy-Item -LiteralPath $config -Destination (Join-Path $payload 'launcher.json')
$files = @(Get-ChildItem -LiteralPath $payload -File)
if ($files.Count -ne 2) { throw 'Expected only ShinshiLauncher.exe and launcher.json in the payload.' }
$zip = Join-Path $work 'launcher.zip'
Compress-Archive -LiteralPath $files.FullName -DestinationPath $zip
$arguments = @('publish', "$PSScriptRoot/../installer/Shinshi.Setup.csproj", '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-o', (Join-Path $work 'setup'), '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false', "-p:PayloadPath=$zip")
if ($CertificatePath) { $arguments += '-p:CertificatePath=' + (Resolve-Path -LiteralPath $CertificatePath).Path }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }
$built = Join-Path $work 'setup/Shinshi-Setup.exe'
$check = Start-Process -FilePath $built -ArgumentList '--verify-only' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'Embedded installer payload or certificate validation failed.' }
Copy-Item -LiteralPath $built -Destination (Join-Path $output 'Shinshi-Setup.exe') -Force
Write-Output "Installer: $output/Shinshi-Setup.exe"
Write-Output "Payload: $payload"
