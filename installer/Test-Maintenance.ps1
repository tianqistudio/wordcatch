$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Maintenance.ps1') -Mode Library
$checks=@{}
$checks.VersionNormalization=((Convert-AppVersion '0.2.0') -eq (Convert-AppVersion '0.2.0.0'))
$checks.RevisionOrder=((Convert-AppVersion '0.2.0.1') -gt (Convert-AppVersion '0.2.0'))
$checks.UpgradeOrder=((Convert-AppVersion '0.3.0') -gt (Convert-AppVersion '0.2.0'))
foreach ($bad in @('0.2','v0.2.0','1.2.3-beta','1.2.3.4.5')) {
    try { $null=Convert-AppVersion $bad; $checks["RejectVersion-$bad"]=$false } catch { $checks["RejectVersion-$bad"]=$true }
}
$root=Join-Path $env:SystemDrive 'WordLens-policy-test-not-created'
foreach ($bad in @('..\private.txt','C:\private.txt','x:stream','one\\two')) {
    try { $null=Resolve-OwnedPath $root $bad; $checks["RejectPath-$bad"]=$false } catch { $checks["RejectPath-$bad"]=$true }
}
$checks.ValidPath=((Resolve-OwnedPath $root 'worker\app.exe') -eq (Join-Path $root 'worker\app.exe'))
foreach ($desktop in 0,1) { foreach ($menu in 0,1) {
    $plan=@(Get-LinkPlan $root $desktop $menu)
    $checks["Shortcuts-$desktop-$menu"]=($plan[0].Enabled -eq ($desktop -eq 1) -and $plan[1].Enabled -eq ($menu -eq 1))
    $checks.MenuGroup=$plan[1].Path.EndsWith('Programs\tianqistudio\单词捕手.lnk')
} }
$checks.LegacyShortcutsDisabled=($plan.Count -eq 4 -and !$plan[2].Enabled -and !$plan[3].Enabled)
$passed=-not ($checks.Values -contains $false)
@{passed=$passed;checks=$checks;scope='pure version/path/shortcut planning; no installation, registry or deletion'} | ConvertTo-Json -Depth 4
if(-not $passed){exit 1}
