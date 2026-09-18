# Build script: icon -> GUI app -> CLI tool.
# Keep this file ASCII-only: Windows PowerShell 5.1 reads scripts as ANSI (GBK here)
# unless they carry a UTF-8 BOM, which would corrupt non-ASCII text.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$icon = Join-Path $root 'assets\app.ico'
$dist = Join-Path $root 'dist'

# Keep .NET's home/caches inside the repo so the build never depends on (or writes to)
# the user profile.
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:NUGET_PACKAGES = Join-Path $root '.packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not (Test-Path $icon)) {
    & (Join-Path $root 'tools\make-icon.ps1')
}

Write-Host '==> Building GUI app' -ForegroundColor Cyan
dotnet build (Join-Path $root 'src\HdrBrightness\HdrBrightness.csproj') -c Release -o $dist --nologo
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed' }

Write-Host '==> Building CLI tool' -ForegroundColor Cyan
dotnet build (Join-Path $root 'src\HdrBrightness.Cli\hdrbright.csproj') -c Release -o $dist --nologo
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed' }

Write-Host ''
Write-Host "Build finished: $dist" -ForegroundColor Green
Get-ChildItem $dist -Filter '*.exe' | ForEach-Object {
    Write-Host ('  {0}  {1:N0} KB' -f $_.Name, ($_.Length / 1KB))
}
