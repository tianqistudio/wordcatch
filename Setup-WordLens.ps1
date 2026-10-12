param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$env:PYTHONUTF8 = '1'
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.venv/Scripts/python.exe'))) {
    & $Python -m venv (Join-Path $projectRoot '.venv')
    if ($LASTEXITCODE -ne 0) { throw '创建 Python 环境失败；请使用 Python 3.12。' }
}
$projectPython = Join-Path $projectRoot '.venv/Scripts/python.exe'
& $projectPython -m pip install -r (Join-Path $projectRoot 'requirements.lock.txt')
if ($LASTEXITCODE -ne 0) { throw '依赖安装失败。' }
& $projectPython (Join-Path $projectRoot 'tools/prepare_dictionary.py')
if ($LASTEXITCODE -ne 0) { throw '词典准备失败。' }
& $projectPython (Join-Path $projectRoot 'tools/verify_worker.py') --prepare
if ($LASTEXITCODE -ne 0) { throw '识别模型准备失败。' }
dotnet build (Join-Path $projectRoot 'src/WordLens/WordLens.csproj') -v minimal
if ($LASTEXITCODE -ne 0) { throw '桌面程序编译失败。' }
Write-Host '准备完成。运行 Start-WordLens.ps1 启动。'
