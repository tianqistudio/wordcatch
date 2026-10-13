Unicode true
RequestExecutionLevel admin
ManifestDPIAware true
SetCompressor /SOLID lzma
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
Name "单词捕手 · WordCatch"
OutFile "${OUTPUT}"
InstallDir "$PROGRAMFILES64\tianqistudio\WordLens"
BrandingText "tianqistudio · WordCatch"
VIProductVersion "0.5.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "单词捕手 · WordCatch"
VIAddVersionKey /LANG=2052 "CompanyName" "tianqistudio"
VIAddVersionKey /LANG=2052 "FileDescription" "单词捕手 · WordCatch安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "0.5.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "tianqistudio"
ShowInstDetails show
ShowUninstDetails show
Var DesktopChoice
Var MenuChoice
Var DesktopBox
Var MenuBox
!define MUI_ICON "../src/WordLens/Assets/wordcatch.ico"
!define MUI_UNICON "../src/WordLens/Assets/wordcatch.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "安装 单词捕手 · WordCatch 0.5.0。$\r$\n$\r$\n包含本地 OCR、离线词典及全部运行环境，无需另装 Python 或 .NET。$\r$\n$\r$\n请先退出正在运行的单词捕手。Azure 云端备用需要安装后填写自己的密钥，默认关闭。"
!insertmacro MUI_PAGE_WELCOME
Page custom OptionsPage OptionsLeave
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "单词捕手 · WordCatch已安装。$\r$\n$\r$\n请从快捷方式或安装目录打开。选中文字后按 Ctrl + Alt + D 查询；无法读取选区时自动改用鼠标位置 OCR。$\r$\n$\r$\n程序不会开机自启。Azure 翻译在设置中配置并启用。"
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "卸载前请从通知区域退出单词捕手。$\r$\n$\r$\n将移除安装器登记的程序文件和快捷方式。你的快捷键与加密云端配置保留在用户目录。"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "仅支持 Windows x64。"
    Abort
  ${EndIf}
  IfSilent 0 +3
    MessageBox MB_ICONSTOP "此安装程序需要交互确认，不支持静默安装。"
    Abort
  SetRegView 64
  StrCpy $DesktopChoice 1
  StrCpy $MenuChoice 1
  ClearErrors
  ReadRegDWORD $0 HKLM "Software\tianqistudio\WordLens" "DesktopShortcut"
  ${IfNot} ${Errors}
    StrCpy $DesktopChoice $0
  ${EndIf}
  ClearErrors
  ReadRegDWORD $0 HKLM "Software\tianqistudio\WordLens" "StartMenuShortcut"
  ${IfNot} ${Errors}
    StrCpy $MenuChoice $0
  ${EndIf}
FunctionEnd
Function OptionsPage
  !insertmacro MUI_HEADER_TEXT "快捷方式" "两项可独立取消；再次安装时沿用上次选择。"
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateCheckbox} 0 16u 100% 16u "创建桌面快捷方式"
  Pop $DesktopBox
  ${NSD_SetState} $DesktopBox $DesktopChoice
  ${NSD_CreateCheckbox} 0 44u 100% 16u "创建开始菜单快捷方式"
  Pop $MenuBox
  ${NSD_SetState} $MenuBox $MenuChoice
  ${NSD_CreateLabel} 0 82u 100% 50u "开始菜单：tianqistudio$\r$\n安装目录：$INSTDIR$\r$\n本机测试可取消桌面项，仅保留开始菜单。"
  Pop $0
  nsDialogs::Show
FunctionEnd
Function OptionsLeave
  ${NSD_GetState} $DesktopBox $DesktopChoice
  ${NSD_GetState} $MenuBox $MenuChoice
FunctionEnd
Section "安装"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\payload"
  File /r "${PAYLOAD}\*"
  WriteUninstaller "$PLUGINSDIR\payload\Uninstall.exe"
  ${DisableX64FSRedirection}
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\payload\Maintenance.ps1" -Mode Install -Payload "$PLUGINSDIR\payload" -Desktop $DesktopChoice -StartMenu $MenuChoice'
  Pop $0
  ${EnableX64FSRedirection}
  ${If} $0 != 0
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "安装未完成。请查看详细信息；旧版本入口与用户配置保持不变。"
    Abort
  ${EndIf}
SectionEnd
Section "Uninstall"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "${PAYLOAD}\Maintenance.ps1"
  ${DisableX64FSRedirection}
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Maintenance.ps1" -Mode Uninstall'
  Pop $0
  ${EnableX64FSRedirection}
  ${If} $0 != 0
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "卸载未完成，请查看详细信息。用户配置仍保留。"
    Abort
  ${EndIf}
SectionEnd
