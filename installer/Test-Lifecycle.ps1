param([Parameter(Mandatory=$true)][string]$TestRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Maintenance.ps1') -Mode Library
$script:qaRoot=[IO.Path]::GetFullPath($TestRoot)
$allowed=Join-Path $env:LOCALAPPDATA 'Codex\WordLens-QA'
if (-not $script:qaRoot.StartsWith($allowed+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture root must be inside the dedicated local WordLens QA directory.' }
if(Test-Path -LiteralPath $script:qaRoot){throw 'Use a fresh fixture directory.'}
New-Item -ItemType Directory -Path $script:qaRoot | Out-Null
$script:registry=@{}; $script:failRegistry=$false
function Assert-AppStopped { }
function Get-InstallRoot { return (Join-Path $script:qaRoot 'installed') }
function Get-LinkPlan([string]$Root,[int]$Desktop,[int]$StartMenu) {
    @([pscustomobject]@{Path=(Join-Path $script:qaRoot 'links\desktop.lnk');Enabled=($Desktop -eq 1)},[pscustomobject]@{Path=(Join-Path $script:qaRoot 'links\menu.lnk');Enabled=($StartMenu -eq 1)},[pscustomobject]@{Path=(Join-Path $script:qaRoot 'links\legacy.lnk');Enabled=$false})
}
function Get-RegSnapshot([string]$Path) { if($script:registry.ContainsKey($Path)){return $script:registry[$Path].Clone()};return $null }
function Restore-RegSnapshot([string]$Path,$Snapshot) { if($null -eq $Snapshot){$script:registry.Remove($Path)}else{$script:registry[$Path]=$Snapshot.Clone()} }
function New-Item {
    param([string]$Path,[string]$ItemType,[switch]$Force)
    if($Path.StartsWith('HKLM:')){if(-not $script:registry.ContainsKey($Path)){$script:registry[$Path]=@{}};return}
    Microsoft.PowerShell.Management\New-Item -Path $Path -ItemType $ItemType -Force:$Force
}
function New-ItemProperty {
    param([string]$LiteralPath,[string]$Name,$Value,$PropertyType,[switch]$Force)
    if(-not $LiteralPath.StartsWith('HKLM:')){throw 'Unexpected registry target'}
    if($script:failRegistry){throw 'Synthetic registry failure'}
    $script:registry[$LiteralPath][$Name]=@($Value,$PropertyType)
}
function Test-Path {
    param([Alias('Path')][string]$LiteralPath,[string]$PathType)
    if($LiteralPath.StartsWith('HKLM:')){return $script:registry.ContainsKey($LiteralPath)}
    if($PathType){Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType}else{Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath}
}
function Remove-Item {
    param([string]$LiteralPath,[switch]$Force)
    if($LiteralPath.StartsWith('HKLM:')){$script:registry.Remove($LiteralPath);return}
    $resolved=[IO.Path]::GetFullPath($LiteralPath)
    if(-not $resolved.StartsWith($script:qaRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture deletion escaped root'}
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $resolved -Force:$Force
}
function Make-Payload([string]$Version) {
    $script:Payload=Join-Path $script:qaRoot ('payload-'+$Version)
    New-Item -ItemType Directory -Path $script:Payload -Force | Out-Null
    foreach($name in 'WordLens.exe','使用说明.md','Uninstall.exe') {
        [IO.File]::WriteAllText((Join-Path $script:Payload $name),('synthetic-only-'+$Version+'-'+$name),[Text.UTF8Encoding]::new($false))
    }
    $files=@('WordLens.exe','使用说明.md' | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $script:Payload $_)).Hash} })
    [IO.File]::WriteAllText((Join-Path $script:Payload 'payload.json'),(@{product='WordLens';version=$Version;files=$files}|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
}
$checks=@{}
Make-Payload '0.2.0'
$script:Desktop=0;$script:StartMenu=1
Invoke-WordLensInstall | Out-Null
$root=Get-InstallRoot;$statePath=Join-Path $root 'install-state.json'
$initial=Get-Content -LiteralPath $statePath -Encoding UTF8 -Raw | ConvertFrom-Json
$checks.FirstInstall=(Test-Path -LiteralPath (Join-Path $root ($initial.current+'\使用说明.md')))
$checks.MenuOnly=(!(Test-Path -LiteralPath (Join-Path $script:qaRoot 'links\desktop.lnk')) -and (Test-Path -LiteralPath (Join-Path $script:qaRoot 'links\menu.lnk')))
Copy-Item -LiteralPath (Join-Path $script:qaRoot 'links\menu.lnk') -Destination (Join-Path $script:qaRoot 'links\legacy.lnk')
Invoke-WordLensInstall | Out-Null
$checks.LegacyShortcutRemoved=(!(Test-Path -LiteralPath (Join-Path $script:qaRoot 'links\legacy.lnk')))
$same=Get-Content -LiteralPath $statePath -Encoding UTF8 -Raw | ConvertFrom-Json
$checks.SameVersionNewDirectory=($same.current -ne $initial.current -and $same.versions.Count -eq 2)
$beforeState=[IO.File]::ReadAllText($statePath)
$beforeLink=(Get-FileHash -LiteralPath (Join-Path $script:qaRoot 'links\menu.lnk')).Hash
Copy-Item -LiteralPath (Join-Path $script:qaRoot 'links\menu.lnk') -Destination (Join-Path $script:qaRoot 'links\legacy.lnk')
$script:failRegistry=$true
try{Invoke-WordLensInstall | Out-Null;$checks.RegistryFailure=$false}catch{$checks.RegistryFailure=$_.Exception.Message.Contains('Synthetic')}
$script:failRegistry=$false
$checks.LegacyShortcutRestored=(Test-Path -LiteralPath (Join-Path $script:qaRoot 'links\legacy.lnk'))
$checks.RollbackState=([IO.File]::ReadAllText($statePath) -eq $beforeState)
$checks.RollbackLink=((Get-FileHash -LiteralPath (Join-Path $script:qaRoot 'links\menu.lnk')).Hash -eq $beforeLink)
Make-Payload '0.1.0'
try{Invoke-WordLensInstall | Out-Null;$checks.DowngradeRejected=$false}catch{$checks.DowngradeRejected=$true}
Make-Payload '0.3.0'
$script:Desktop=0;$script:StartMenu=0
Invoke-WordLensInstall | Out-Null
$latest=Get-Content -LiteralPath $statePath -Encoding UTF8 -Raw | ConvertFrom-Json
$checks.Upgrade=($latest.version -eq '0.3.0' -and $latest.versions.Count -eq 3)
$checks.BothShortcutsOff=(!(Test-Path -LiteralPath (Join-Path $script:qaRoot 'links\desktop.lnk')) -and !(Test-Path -LiteralPath (Join-Path $script:qaRoot 'links\menu.lnk')))
$unknown=Join-Path $root 'user-created.txt';[IO.File]::WriteAllText($unknown,'protected fixture')
Invoke-WordLensUninstall | Out-Null
$checks.UninstallStateRemoved=(!(Test-Path -LiteralPath $statePath))
$checks.UnknownFilePreserved=(Test-Path -LiteralPath $unknown)
$checks.FakeRegistryRemoved=($script:registry.Count -eq 0)
$passed=-not ($checks.Values -contains $false)
@{passed=$passed;checks=$checks;root=$script:qaRoot;scope='isolated filesystem and real fixture shortcuts; fake registry, no host installation'} | ConvertTo-Json -Depth 5
if(-not $passed){exit 1}
