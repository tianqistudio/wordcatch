Unicode true
RequestExecutionLevel admin
ManifestDPIAware true
SetCompressor /SOLID lzma
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
Name "$(String0)"
OutFile "${OUTPUT}"
InstallDir "$PROGRAMFILES64\tianqistudio\WordLens"
BrandingText "tianqistudio · WordCatch"
VIProductVersion "0.7.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "单词捕手 · WordCatch"
VIAddVersionKey /LANG=1033 "ProductName" "WordCatch"
VIAddVersionKey /LANG=2052 "CompanyName" "tianqistudio"
VIAddVersionKey /LANG=1033 "CompanyName" "tianqistudio"
VIAddVersionKey /LANG=2052 "FileDescription" "单词捕手 · WordCatch安装程序"
VIAddVersionKey /LANG=1033 "FileDescription" "WordCatch Installer"
VIAddVersionKey /LANG=2052 "FileVersion" "0.7.0"
VIAddVersionKey /LANG=1033 "FileVersion" "0.7.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "tianqistudio"
VIAddVersionKey /LANG=1033 "LegalCopyright" "tianqistudio"
ShowInstDetails show
ShowUninstDetails show
Var DesktopChoice
Var MenuChoice
Var DesktopBox
Var MenuBox
!define MUI_ICON "../src/WordLens/Assets/wordcatch.ico"
!define MUI_UNICON "../src/WordLens/Assets/wordcatch.ico"
!define MUI_LANGDLL_REGISTRY_ROOT HKLM
!define MUI_LANGDLL_REGISTRY_KEY "Software\tianqistudio\WordLens"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!define MUI_LANGDLL_ALWAYSSHOW
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "$(String2)"
!insertmacro MUI_PAGE_WELCOME
Page custom OptionsPage OptionsLeave
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "$(String3)"
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "$(String4)"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"
LangString String0 ${LANG_SIMPCHINESE} "单词捕手 · WordCatch"
LangString String0 ${LANG_ENGLISH} "WordCatch"
LangString String1 ${LANG_SIMPCHINESE} "单词捕手 · WordCatch安装程序"
LangString String1 ${LANG_ENGLISH} "WordCatch Installer"
LangString String2 ${LANG_SIMPCHINESE} "安装 单词捕手 · WordCatch 0.7.0。$\r$\n$\r$\n包含本地 OCR、离线词典及全部运行环境，无需另装 Python 或 .NET。$\r$\n$\r$\n请先退出正在运行的单词捕手。Azure 云端备用需要安装后填写自己的密钥，默认关闭。"
LangString String2 ${LANG_ENGLISH} "Install WordCatch 0.7.0.$\r$\n$\r$\nIncludes local OCR, an offline dictionary and all runtimes. No separate Python or .NET installation is needed.$\r$\n$\r$\nExit WordCatch before installing. Optional Azure translation requires your own API key and is off by default."
LangString String3 ${LANG_SIMPCHINESE} "单词捕手 · WordCatch已安装。$\r$\n$\r$\n请从快捷方式或安装目录打开。选中文字后按 Ctrl + Alt + D 查询；无法读取选区时自动改用鼠标位置 OCR。$\r$\n$\r$\n开机自启动默认关闭，可在设置中开启。Azure 翻译在设置中配置并启用。"
LangString String3 ${LANG_ENGLISH} "WordCatch is installed.$\r$\n$\r$\nOpen it from a shortcut or the install folder. Select text and press Ctrl + Alt + D; OCR at the pointer is used when selection capture is unavailable.$\r$\n$\r$\nChoose Chinese or English in Settings & About. Startup and optional Azure translation are configured there."
LangString String4 ${LANG_SIMPCHINESE} "卸载前请从通知区域退出单词捕手。$\r$\n$\r$\n将移除安装器登记的程序文件和快捷方式。你的快捷键与加密云端配置保留在用户目录。"
LangString String4 ${LANG_ENGLISH} "Exit WordCatch from the notification area before uninstalling.$\r$\n$\r$\nRegistered application files and shortcuts will be removed. Your shortcuts, language and encrypted cloud settings remain in your user folder."
LangString String5 ${LANG_SIMPCHINESE} "仅支持 Windows x64。"
LangString String5 ${LANG_ENGLISH} "Windows x64 is required."
LangString String6 ${LANG_SIMPCHINESE} "此安装程序需要交互确认，不支持静默安装。"
LangString String6 ${LANG_ENGLISH} "This installer requires interaction; silent installation is not supported."
LangString String7 ${LANG_SIMPCHINESE} "快捷方式"
LangString String7 ${LANG_ENGLISH} "Shortcuts"
LangString String8 ${LANG_SIMPCHINESE} "两项可独立取消；再次安装时沿用上次选择。"
LangString String8 ${LANG_ENGLISH} "Select each independently. Reinstalling keeps your previous choices."
LangString String9 ${LANG_SIMPCHINESE} "创建桌面快捷方式"
LangString String9 ${LANG_ENGLISH} "Create a desktop shortcut"
LangString String10 ${LANG_SIMPCHINESE} "创建开始菜单快捷方式"
LangString String10 ${LANG_ENGLISH} "Create a Start menu shortcut"
LangString String11 ${LANG_SIMPCHINESE} "开始菜单：tianqistudio$\r$\n安装目录：$INSTDIR$\r$\n本机测试可取消桌面项，仅保留开始菜单。"
LangString String11 ${LANG_ENGLISH} "Start menu: tianqistudio$\r$\nInstall folder: $INSTDIR"
LangString String12 ${LANG_SIMPCHINESE} "安装"
LangString String12 ${LANG_ENGLISH} "Install"
LangString String13 ${LANG_SIMPCHINESE} "安装未完成。请查看详细信息；旧版本入口与用户配置保持不变。"
LangString String13 ${LANG_ENGLISH} "Installation did not finish. See the details; existing entry points and user settings are unchanged."
LangString String14 ${LANG_SIMPCHINESE} "卸载未完成，请查看详细信息。用户配置仍保留。"
LangString String14 ${LANG_ENGLISH} "Uninstallation did not finish. See the details. User settings are retained."

Function .onInit
  SetRegView 64
  !insertmacro MUI_LANGDLL_DISPLAY
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "$(String5)"
    Abort
  ${EndIf}
  IfSilent 0 +3
    MessageBox MB_ICONSTOP "$(String6)"
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
  !insertmacro MUI_HEADER_TEXT "$(String7)" "$(String8)"
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateCheckbox} 0 16u 100% 16u "$(String9)"
  Pop $DesktopBox
  ${NSD_SetState} $DesktopBox $DesktopChoice
  ${NSD_CreateCheckbox} 0 44u 100% 16u "$(String10)"
  Pop $MenuBox
  ${NSD_SetState} $MenuBox $MenuChoice
  ${NSD_CreateLabel} 0 82u 100% 50u "$(String11)"
  Pop $0
  nsDialogs::Show
FunctionEnd
Function OptionsLeave
  ${NSD_GetState} $DesktopBox $DesktopChoice
  ${NSD_GetState} $MenuBox $MenuChoice
FunctionEnd
Section "$(String12)"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR\payload"
  File /r "${PAYLOAD}\*"
  WriteUninstaller "$PLUGINSDIR\payload\Uninstall.exe"
  ${DisableX64FSRedirection}
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\payload\Maintenance.ps1" -Mode Install -Language $LANGUAGE -Payload "$PLUGINSDIR\payload" -Desktop $DesktopChoice -StartMenu $MenuChoice'
  Pop $0
  ${EnableX64FSRedirection}
  ${If} $0 != 0
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "$(String13)"
    Abort
  ${EndIf}
SectionEnd
Section "Uninstall"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File "${PAYLOAD}\Maintenance.ps1"
  ${DisableX64FSRedirection}
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Maintenance.ps1" -Mode Uninstall -Language $LANGUAGE'
  Pop $0
  ${EnableX64FSRedirection}
  ${If} $0 != 0
    SetErrorLevel 1
    MessageBox MB_ICONSTOP "$(String14)"
    Abort
  ${EndIf}
SectionEnd

Function un.onInit
  SetRegView 64
  !insertmacro MUI_UNGETLANGUAGE
FunctionEnd
