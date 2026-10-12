$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$applicationPath = Join-Path $projectRoot 'artifacts/installers/0.4.0-001/payload/WordLens.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) { $applicationPath = Join-Path $projectRoot 'src/WordLens/bin/Debug/net10.0-windows/WordLens.exe' }
if (-not (Test-Path -LiteralPath $applicationPath)) { throw '尚未编译，请先运行 Setup-WordLens.ps1。' }
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'data/ecdict.sqlite3'))) { throw '尚未准备词典，请先运行 Setup-WordLens.ps1。' }
Start-Process -FilePath $applicationPath -WorkingDirectory $projectRoot -WindowStyle Hidden
