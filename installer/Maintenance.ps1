param([ValidateSet('Install','Uninstall','Library')][string]$Mode='Library', [string]$Payload,
      [ValidateSet(0,1)][int]$Desktop=1, [ValidateSet(0,1)][int]$StartMenu=1)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

function Convert-AppVersion([string]$Text) {
    if ($Text -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw '版本格式不正确。' }
    $parts=$Text.Split('.')
    if ($parts.Count -eq 3) { $Text += '.0' }
    return [Version]::Parse($Text)
}
function Get-InstallRoot { return (Join-Path $env:ProgramFiles 'tianqistudio\WordLens') }
function Assert-NoReparse([string]$Path) {
    $itemPath=[IO.Path]::GetFullPath($Path)
    while ($itemPath) {
        if (Test-Path -LiteralPath $itemPath) {
            if ((Get-Item -LiteralPath $itemPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "路径含重解析点：$itemPath" }
        }
        $parent=[IO.Path]::GetDirectoryName($itemPath)
        if ($parent -eq $itemPath) { break }; $itemPath=$parent
    }
}
function Resolve-OwnedPath([string]$Root,[string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or @($Relative -split '[\\/]' | Where-Object { $_ -eq '..' -or $_ -eq '' }).Count -gt 0) { throw '清单路径不合法。' }
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\'
    $result=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $result.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) { throw '清单路径越界。' }
    Assert-NoReparse $result
    return $result
}
function Assert-AppStopped {
    $mutex=$null
    try { $mutex=[Threading.Mutex]::OpenExisting('Local\TianqiStudio.WordLens') }
    catch [Threading.WaitHandleCannotBeOpenedException] { return }
    catch [UnauthorizedAccessException] { throw '单词捕手可能正在其他权限级别运行，请先退出。' }
    if ($mutex) { $mutex.Dispose(); throw '请先从通知区域退出单词捕手，再继续。' }
}
function Get-LinkPlan([string]$Root,[int]$Desktop,[int]$StartMenu) {
    @(
        [pscustomobject]@{Path=(Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) '单词捕手.lnk'); Enabled=($Desktop -eq 1)},
        [pscustomobject]@{Path=(Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'tianqistudio\单词捕手.lnk'); Enabled=($StartMenu -eq 1)},
        [pscustomobject]@{Path=(Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'WordLens 词镜.lnk'); Enabled=$false},
        [pscustomobject]@{Path=(Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'tianqistudio\WordLens 词镜.lnk'); Enabled=$false}
    )
}
function Assert-LinkOwned([string]$Path,[string]$Root) {
    Assert-NoReparse $Path
    if (Test-Path -LiteralPath $Path) {
        $shell=New-Object -ComObject WScript.Shell
        $target=$shell.CreateShortcut($Path).TargetPath
        if (-not $target.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw "同名快捷方式不属于单词捕手，停止覆盖：$Path" }
    }
}
function Get-RegSnapshot([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $key=Get-Item -LiteralPath $Path; $values=@{}
    foreach ($name in $key.GetValueNames()) { $values[$name]=@($key.GetValue($name),$key.GetValueKind($name)) }
    return $values
}
function Restore-RegSnapshot([string]$Path,$Snapshot) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    if ($null -ne $Snapshot) {
        New-Item -Path $Path -Force | Out-Null
        foreach ($name in $Snapshot.Keys) { New-ItemProperty -LiteralPath $Path -Name $name -Value $Snapshot[$name][0] -PropertyType $Snapshot[$name][1] -Force | Out-Null }
    }
}
function Get-StartupCommand {
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try { if($key){return $key.GetValue('TianqiStudio.WordLens')} } finally { if($key){$key.Dispose()} }
}
function Set-StartupCommand($Command) {
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        if($null -eq $Command){$key.DeleteValue('TianqiStudio.WordLens',$false)}
        else {$key.SetValue('TianqiStudio.WordLens',$Command,[Microsoft.Win32.RegistryValueKind]::String)}
    } finally {$key.Dispose()}
}
function Test-OwnedStartup($Command,[string]$Root) {
    if($Command -isnot [string] -or $Command -notmatch '^"([^"\r\n]+)" --startup$'){return $false}
    $exe=[IO.Path]::GetFullPath($Matches[1])
    return $exe.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($exe) -eq 'WordLens.exe'
}
function Invoke-WordLensInstall {
    $root=Get-InstallRoot
    Assert-NoReparse $root; Assert-NoReparse $Payload; Assert-AppStopped
    $statePath=Join-Path $root 'install-state.json'
    $reg='HKLM:\SOFTWARE\tianqistudio\WordLens'
    $unreg='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WordLens'
    $manifest=Get-Content -LiteralPath (Join-Path $Payload 'payload.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.product -ne 'WordLens') { throw '载荷标识不符。' }
    $version=Convert-AppVersion $manifest.version
    $oldState=$null; $stateBytes=$null
    if (Test-Path -LiteralPath $statePath) {
        $stateBytes=[IO.File]::ReadAllBytes($statePath)
        $oldState=[Text.Encoding]::UTF8.GetString($stateBytes) | ConvertFrom-Json
        if ($oldState.product -ne 'WordLens') { throw '已有目录不是词镜安装目录。' }
        if ($version -lt (Convert-AppVersion $oldState.version)) { throw '不支持降级安装，请使用相同或更新版本。' }
    } elseif (Test-Path -LiteralPath $root) {
        $marker=Join-Path $root 'wordlens-owner.txt'
        if (@(Get-ChildItem -LiteralPath $root -Force).Count -gt 0 -and (-not (Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw) -ne 'WordLens Installer')) { throw '安装目录非空且无单词捕手清单，停止覆盖。' }
    }
    foreach ($file in $manifest.files) {
        $source=Resolve-OwnedPath $Payload $file.path
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.sha256) { throw "载荷校验失败：$($file.path)" }
    }
    $links=@(Get-LinkPlan $root $Desktop $StartMenu)
    $savedLinks=@{}
    foreach ($link in $links) {
        Assert-LinkOwned $link.Path $root
        $savedLinks[$link.Path]=if(Test-Path -LiteralPath $link.Path){[IO.File]::ReadAllBytes($link.Path)}else{$null}
    }
    $startupBackup=Get-StartupCommand; $startupChanged=$false
    $regBackup=Get-RegSnapshot $reg; $unregBackup=Get-RegSnapshot $unreg
    $folder='versions\'+$manifest.version+'-'+[Guid]::NewGuid().ToString('N')
    $destination=Resolve-OwnedPath $root $folder
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'wordlens-owner.txt'),'WordLens Installer',[Text.UTF8Encoding]::new($false))
    $installed=@()
    # Each attempt uses a fresh directory. Until pointer commit, the old installation is untouched.
    foreach ($file in $manifest.files) {
        $target=Resolve-OwnedPath $destination $file.path
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $Payload $file.path) -Destination $target
        if ((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256) { throw "写入校验失败：$($file.path)" }
        $installed += [pscustomobject]@{path=$file.path;sha256=$file.sha256}
    }
    $uninstaller=Join-Path $destination 'Uninstall.exe'
    Copy-Item -LiteralPath (Join-Path $Payload 'Uninstall.exe') -Destination $uninstaller
    $installed += [pscustomobject]@{path='Uninstall.exe';sha256=(Get-FileHash -LiteralPath $uninstaller).Hash}
    $versions=@()
    if ($null -ne $oldState) { $versions=@($oldState.versions) }
    $versions += [pscustomobject]@{path=$folder;files=$installed}
    $state=@{product='WordLens';version=$manifest.version;current=$folder;versions=$versions}
    try {
        $shell=New-Object -ComObject WScript.Shell
        foreach ($link in $links) {
            if ($link.Enabled) {
                New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($link.Path)) -Force | Out-Null
                $shortcut=$shell.CreateShortcut($link.Path); $shortcut.TargetPath=Join-Path $destination 'WordLens.exe'; $shortcut.WorkingDirectory=$destination; $shortcut.Save()
                if ($shell.CreateShortcut($link.Path).TargetPath -ne (Join-Path $destination 'WordLens.exe')) { throw '快捷方式回读不符。' }
            } elseif (Test-Path -LiteralPath $link.Path) { Remove-Item -LiteralPath $link.Path -Force }
        }
        New-Item -Path $reg -Force | Out-Null; New-Item -Path $unreg -Force | Out-Null
        foreach ($pair in @{DesktopShortcut=$Desktop;StartMenuShortcut=$StartMenu}.GetEnumerator()) { New-ItemProperty -LiteralPath $reg -Name $pair.Key -Value $pair.Value -PropertyType DWord -Force | Out-Null }
        foreach ($pair in @{DisplayName='单词捕手 · WordCatch';DisplayVersion=$manifest.version;Publisher='tianqistudio';InstallLocation=$root;UninstallString=('"'+$uninstaller+'"');DisplayIcon=(Join-Path $destination 'WordLens.exe')}.GetEnumerator()) { New-ItemProperty -LiteralPath $unreg -Name $pair.Key -Value $pair.Value -PropertyType String -Force | Out-Null }
        foreach ($name in 'NoModify','NoRepair') { New-ItemProperty -LiteralPath $unreg -Name $name -Value 1 -PropertyType DWord -Force | Out-Null }
        if(Test-OwnedStartup $startupBackup $root) {
            $startupChanged=$true
            Set-StartupCommand ('"'+(Join-Path $destination 'WordLens.exe')+'" --startup')
        }
        $json=$state | ConvertTo-Json -Depth 8
        [IO.File]::WriteAllText($statePath+'.new',$json,[Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $statePath) { [IO.File]::Replace($statePath+'.new',$statePath,[System.Management.Automation.Language.NullString]::Value) } else { [IO.File]::Move($statePath+'.new',$statePath) }
    } catch {
        $failure=$_
        if($startupChanged){Set-StartupCommand $startupBackup}
        foreach ($link in $links) {
            if ($null -ne $savedLinks[$link.Path]) { [IO.File]::WriteAllBytes($link.Path,$savedLinks[$link.Path]) }
            elseif (Test-Path -LiteralPath $link.Path) { Remove-Item -LiteralPath $link.Path -Force }
        }
        Restore-RegSnapshot $reg $regBackup; Restore-RegSnapshot $unreg $unregBackup
        if ($null -ne $stateBytes) { [IO.File]::WriteAllBytes($statePath,$stateBytes) }
        throw $failure
    }
    Write-Output ('安装完成：'+$destination)
}
function Invoke-WordLensUninstall {
    $root=Get-InstallRoot
    Assert-NoReparse $root; Assert-AppStopped
    $statePath=Join-Path $root 'install-state.json'
    $state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.product -ne 'WordLens') { throw '安装标识不符。' }
    $files=@();$directories=@()
    foreach ($version in $state.versions) {
        if ($version.path -notmatch '^versions\\\d+\.\d+\.\d+-[a-f0-9]{32}$') { throw '安装版本路径不合法。' }
        $base=Resolve-OwnedPath $root $version.path; $directories += $base
        foreach ($entry in $version.files) {
            $path=Resolve-OwnedPath $base $entry.path
            if (Test-Path -LiteralPath $path) {
                if ((Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw "程序文件已变化，停止卸载并保留：$path" }
                $files += $path
                $parent=[IO.Path]::GetDirectoryName($path)
                while ($parent.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)) { $directories += $parent; $parent=[IO.Path]::GetDirectoryName($parent) }
            }
        }
    }
    $links=@(Get-LinkPlan $root 0 0)
    foreach ($link in $links) { Assert-LinkOwned $link.Path $root }
    $startupCommand=Get-StartupCommand
    if(Test-OwnedStartup $startupCommand $root){Set-StartupCommand $null}
    foreach ($path in $files) { Remove-Item -LiteralPath $path -Force }
    foreach ($directory in ($directories | Sort-Object Length -Descending -Unique)) {
        if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { Remove-Item -LiteralPath $directory }
    }
    foreach ($link in $links) { if(Test-Path -LiteralPath $link.Path){Remove-Item -LiteralPath $link.Path -Force} }
    foreach ($reg in 'HKLM:\SOFTWARE\tianqistudio\WordLens','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WordLens') { if(Test-Path -LiteralPath $reg){Remove-Item -LiteralPath $reg -Force} }
    Remove-Item -LiteralPath $statePath -Force
    $marker=Join-Path $root 'wordlens-owner.txt'
    if ((Test-Path -LiteralPath $marker) -and (Get-Content -LiteralPath $marker -Raw) -eq 'WordLens Installer') { Remove-Item -LiteralPath $marker -Force }
    foreach ($directory in @((Join-Path $root 'versions'),$root)) { if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) { Remove-Item -LiteralPath $directory } }
    Write-Output '已移除登记的程序文件。用户配置和未知文件保持不变。'
}
if ($Mode -ne 'Library') {
    $setupMutex=$null; $created=$false
    try {
        $setupMutex=[Threading.Mutex]::new($true,'Global\tianqistudio.WordLens.Setup',[ref]$created)
        if(-not $created){throw '另一个词镜安装或卸载程序正在运行。'}
        if ($Mode -eq 'Install') { Invoke-WordLensInstall } else { Invoke-WordLensUninstall }
    }
    catch { Write-Output ('错误：'+$_.Exception.Message); exit 1 }
    finally { if($setupMutex){if($created){$setupMutex.ReleaseMutex()};$setupMutex.Dispose()} }
}
