; File Merge Assistant setup program: a per-user installation that needs no administrator rights.
; Built by scripts/windows/Build-Installer.ps1, which defines VERSION, CONFIGURATION, STAGE, OUTFILE, PUBLISHER and
; REPOSITORY. STAGE holds app\ (the self-contained GUI with its license files) and cli\fma.exe.
;
; Layout: $INSTDIR\app\, optional $INSTDIR\cli\fma.exe and $INSTDIR\uninstall.exe. Setup and uninstall remove only
; what this program owns: app\ when it carries the marker file setup writes (or the registry records this folder as
; the installation), and fma.exe when the registry records that setup installed it. User data, which the app keeps in
; %LOCALAPPDATA%\FileMergeAssistant (recycle bin, cache, logs, results), is never touched.
;
; Command line, besides the standard /S and /D=:
;   /WAITPID=<pid>  wait for that process to exit first; the app passes its own PID when it starts an update.
;   /RELAUNCH       start the app when setup finishes, or start the existing app again when setup fails.
;   /CLI            also install the command-line tool, for scripted installations.

Unicode true
ManifestDPIAware true
RequestExecutionLevel user
SetCompressor /SOLID lzma

; "|" is true when any of these is undefined.
!ifndef VERSION | STAGE | OUTFILE | CONFIGURATION | PUBLISHER | REPOSITORY
  !error "Build with scripts/windows/Build-Installer.ps1."
!endif

!define APP_NAME "File Merge Assistant"
!define APP_EXE "FileMergeAssistant.exe"
!define CLI_EXE "fma.exe"
!define OWNER_MARKER ".installed-by-file-merge-assistant-setup"
!define REG_APP "Software\FileMergeAssistant"
!define REG_UNINSTALL "Software\Microsoft\Windows\CurrentVersion\Uninstall\FileMergeAssistant"
; The app writes this value when "Start File Merge Assistant when you sign in" is on (StartupRegistration.cs).
!define REG_RUN "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUN_VALUE "FileMergeAssistant"
!define POWERSHELL "$SYSDIR\WindowsPowerShell\v1.0\powershell.exe"
!if "${CONFIGURATION}" == "Release"
  !define DISPLAY_NAME "${APP_NAME}"
!else
  !define DISPLAY_NAME "${APP_NAME} (${CONFIGURATION} build)"
!endif

Name "${DISPLAY_NAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\FileMergeAssistant"
InstallDirRegKey HKCU "${REG_APP}" "InstallDir"
BrandingText "${DISPLAY_NAME} ${VERSION}"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinMessages.nsh"
!include "Sections.nsh"

Var WaitPid
Var Relaunch
Var PreviousInstallDir
Var PreviousCli

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\${APP_EXE}"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${STAGE}\app\LICENSE.txt"
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipDirectoryOnUpgrade
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; The first language is the fallback; Windows set to Traditional Chinese gets the second.
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "TradChinese"

LangString SEC_APP_NAME ${LANG_ENGLISH} "File Merge Assistant"
LangString SEC_APP_NAME ${LANG_TRADCHINESE} "File Merge Assistant"
LangString SEC_APP_DESC ${LANG_ENGLISH} "The application and a Start menu shortcut."
LangString SEC_APP_DESC ${LANG_TRADCHINESE} "應用程式與開始功能表捷徑。"
LangString SEC_CLI_NAME ${LANG_ENGLISH} "Command-line tool (fma)"
LangString SEC_CLI_NAME ${LANG_TRADCHINESE} "命令列工具（fma）"
LangString SEC_CLI_DESC ${LANG_ENGLISH} "Installs fma.exe and adds its folder to your PATH."
LangString SEC_CLI_DESC ${LANG_TRADCHINESE} "安裝 fma.exe，並把它的資料夾加入你的 PATH。"
LangString SEC_DESKTOP_NAME ${LANG_ENGLISH} "Desktop shortcut"
LangString SEC_DESKTOP_NAME ${LANG_TRADCHINESE} "桌面捷徑"
LangString SEC_DESKTOP_DESC ${LANG_ENGLISH} "Adds a shortcut to your desktop."
LangString SEC_DESKTOP_DESC ${LANG_TRADCHINESE} "在桌面加入捷徑。"
LangString MSG_RUNNING ${LANG_ENGLISH} "File Merge Assistant or fma is running. Close it (File Merge Assistant may be in the tray: right-click its icon and select Exit), then select Retry."
LangString MSG_RUNNING ${LANG_TRADCHINESE} "File Merge Assistant 或 fma 正在執行。請先關閉它（File Merge Assistant 可能在系統匣：在圖示上按右鍵，選「結束 File Merge Assistant」），再按「重試」。"
LangString MSG_STILL_RUNNING ${LANG_ENGLISH} "File Merge Assistant or fma is still running, so nothing was changed."
LangString MSG_STILL_RUNNING ${LANG_TRADCHINESE} "File Merge Assistant 或 fma 仍在執行，所以沒有做任何變更。"
LangString MSG_FOREIGN ${LANG_ENGLISH} "$INSTDIR\$R9 already exists and was not installed by this setup program, so nothing was changed. Choose another installation folder."
LangString MSG_FOREIGN ${LANG_TRADCHINESE} "$INSTDIR\$R9 已經存在，而且不是這個安裝程式安裝的，所以沒有做任何變更。請選擇其他安裝位置。"
LangString MSG_PATH_FAILED ${LANG_ENGLISH} "Could not update PATH. You can still run fma from $INSTDIR\cli."
LangString MSG_PATH_FAILED ${LANG_TRADCHINESE} "無法更新 PATH。你仍可以從 $INSTDIR\cli 執行 fma。"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=${LANG_ENGLISH} "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileDescription" "${DISPLAY_NAME} Setup"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "LegalCopyright" "${PUBLISHER}"

; Waits for the process given by /WAITPID (the app that started this update) to exit, for at most 60 seconds.
Function WaitForCaller
  ${If} $WaitPid != ""
    ; SYNCHRONIZE = 0x00100000
    System::Call 'kernel32::OpenProcess(i 0x00100000, i 0, i $WaitPid) p .r0'
    ${If} $0 <> 0
      System::Call 'kernel32::WaitForSingleObject(p r0, i 60000) i .r1'
      System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
  ${EndIf}
FunctionEnd

; Running programs keep their executable locked, so a file that cannot be opened for writing is in use.
; Sets $R6 to 1 when $INSTDIR\app\FileMergeAssistant.exe or $INSTDIR\cli\fma.exe is in use.
!macro FILES_IN_USE PREFIX
Function ${PREFIX}FilesInUse
  StrCpy $R6 0
  ${If} ${FileExists} "$INSTDIR\app\${APP_EXE}"
    ClearErrors
    FileOpen $R5 "$INSTDIR\app\${APP_EXE}" a
    ${If} ${Errors}
      StrCpy $R6 1
      Return
    ${EndIf}
    FileClose $R5
  ${EndIf}
  ${If} ${FileExists} "$INSTDIR\cli\${CLI_EXE}"
    ClearErrors
    FileOpen $R5 "$INSTDIR\cli\${CLI_EXE}" a
    ${If} ${Errors}
      StrCpy $R6 1
      Return
    ${EndIf}
    FileClose $R5
  ${EndIf}
FunctionEnd

; Interactive setup asks the user to close the program; silent setup waits up to 30 seconds and then stops.
Function ${PREFIX}EnsureAppClosed
  StrCpy $1 0
  check:
    Call ${PREFIX}FilesInUse
    ${If} $R6 = 0
      Return
    ${EndIf}
    ${If} ${Silent}
      IntOp $1 $1 + 1
      ${If} $1 > 30
        SetErrorLevel 2
        Abort "$(MSG_STILL_RUNNING)"
      ${EndIf}
      Sleep 1000
      Goto check
    ${EndIf}
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "$(MSG_RUNNING)" IDRETRY check
    Abort "$(MSG_STILL_RUNNING)"
FunctionEnd
!macroend
!insertmacro FILES_IN_USE ""
!insertmacro FILES_IN_USE "un."

; Ownership checks set $R8 to 1 when the item belongs to this program. app\ and cli\ carry a marker file that setup
; writes before copying anything into them; failing that, the registry must have recorded this folder as the
; installation when setup or uninstall started (and, for cli\, that setup installed the tool).
!macro OWNERSHIP PREFIX
Function ${PREFIX}AppFolderIsOurs
  StrCpy $R8 0
  ${If} ${FileExists} "$INSTDIR\app\${OWNER_MARKER}"
    StrCpy $R8 1
  ${ElseIf} $PreviousInstallDir != ""
  ${AndIf} $PreviousInstallDir == $INSTDIR
    StrCpy $R8 1
  ${EndIf}
FunctionEnd

Function ${PREFIX}CliIsOurs
  StrCpy $R8 0
  ${If} ${FileExists} "$INSTDIR\cli\${OWNER_MARKER}"
    StrCpy $R8 1
  ${ElseIf} $PreviousCli = 1
  ${AndIf} $PreviousInstallDir == $INSTDIR
    StrCpy $R8 1
  ${EndIf}
FunctionEnd
!macroend
!insertmacro OWNERSHIP ""
!insertmacro OWNERSHIP "un."

; The registry entries describe one installation. They belong to this folder when they name it or name none.
Function un.RegistryIsOurs
  StrCpy $R8 0
  ${If} $PreviousInstallDir == ""
  ${OrIf} $PreviousInstallDir == $INSTDIR
    StrCpy $R8 1
  ${EndIf}
FunctionEnd

!macro STOP_FOREIGN FOLDER
  StrCpy $R9 "${FOLDER}"
  MessageBox MB_ICONSTOP "$(MSG_FOREIGN)" /SD IDOK
  SetErrorLevel 2
  Abort "$(MSG_FOREIGN)"
!macroend

!macro WRITE_OWNER_MARKER FOLDER
  FileOpen $0 "$INSTDIR\${FOLDER}\${OWNER_MARKER}" w
  FileWrite $0 "Installed by the ${APP_NAME} setup program. Setup and uninstall replace or remove this folder.$\r$\n"
  FileClose $0
!macroend

!macro SET_USER_PATH MODE
  InitPluginsDir
  File "/oname=$PLUGINSDIR\Set-UserPath.ps1" "Set-UserPath.ps1"
  nsExec::ExecToLog '"${POWERSHELL}" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\Set-UserPath.ps1" -KeyPath Environment -${MODE} "$INSTDIR\cli"'
  Pop $0
  ${If} $0 != 0
    DetailPrint "Set-UserPath.ps1 -${MODE} failed: $0"
    MessageBox MB_ICONEXCLAMATION "$(MSG_PATH_FAILED)" /SD IDOK
  ${EndIf}
  SendMessage ${HWND_BROADCAST} ${WM_SETTINGCHANGE} 0 "STR:Environment" /TIMEOUT=5000
!macroend

!macro REMOVE_CLI
  !insertmacro SET_USER_PATH "Remove"
  Delete "$INSTDIR\cli\${CLI_EXE}"
  Delete "$INSTDIR\cli\${OWNER_MARKER}"
  RMDir "$INSTDIR\cli"
!macroend

Section "!$(SEC_APP_NAME)" SEC_APP
  SectionIn RO
  Call Preflight

  ${If} ${FileExists} "$INSTDIR\app\*.*"
    RMDir /r "$INSTDIR\app"
  ${EndIf}
  SetOutPath "$INSTDIR\app"
  ; The marker comes first, so a copy that is interrupted halfway is still recognized and replaced next time.
  !insertmacro WRITE_OWNER_MARKER "app"
  File /r "${STAGE}\app\*.*"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\app\${APP_EXE}"

  WriteRegStr HKCU "${REG_APP}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${REG_APP}" "Version" "${VERSION}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayName" "${DISPLAY_NAME}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "URLInfoAbout" "https://github.com/${REPOSITORY}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${REG_UNINSTALL}" "DisplayIcon" "$INSTDIR\app\${APP_EXE}"
  WriteRegStr HKCU "${REG_UNINSTALL}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${REG_UNINSTALL}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoModify" 1
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "NoRepair" 1
SectionEnd

Section /o "$(SEC_CLI_NAME)" SEC_CLI
  SetOutPath "$INSTDIR\cli"
  !insertmacro WRITE_OWNER_MARKER "cli"
  File "${STAGE}\cli\${CLI_EXE}"
  !insertmacro SET_USER_PATH "Add"
  WriteRegDWORD HKCU "${REG_APP}" "CliInstalled" 1
SectionEnd

Section /o "$(SEC_DESKTOP_NAME)" SEC_DESKTOP
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\app\${APP_EXE}"
SectionEnd

; Every check runs before anything is changed, so a refused installation leaves the folder as it was.
Function Preflight
  Call WaitForCaller
  Call EnsureAppClosed
  ${If} ${FileExists} "$INSTDIR\app\*.*"
    Call AppFolderIsOurs
    ${If} $R8 <> 1
      !insertmacro STOP_FOREIGN "app"
    ${EndIf}
  ${EndIf}
  ${If} ${SectionIsSelected} ${SEC_CLI}
  ${AndIf} ${FileExists} "$INSTDIR\cli\${CLI_EXE}"
    Call CliIsOurs
    ${If} $R8 <> 1
      !insertmacro STOP_FOREIGN "cli\${CLI_EXE}"
    ${EndIf}
  ${EndIf}
FunctionEnd

; An upgrade without the command-line tool removes the copy an earlier setup installed here, PATH entry included.
Section "-RemoveDeselectedCli"
  ${IfNot} ${SectionIsSelected} ${SEC_CLI}
    ; Checks the folder, not fma.exe, so a PATH entry is cleaned up even when fma.exe was deleted by hand.
    ${If} ${FileExists} "$INSTDIR\cli\*.*"
      Call CliIsOurs
      ${If} $R8 = 1
        !insertmacro REMOVE_CLI
      ${EndIf}
    ${EndIf}
    WriteRegDWORD HKCU "${REG_APP}" "CliInstalled" 0
  ${EndIf}
SectionEnd

Section "-Finish"
  SetOutPath "$INSTDIR"
  ${GetSize} "$INSTDIR\app" "/S=0K" $0 $1 $2
  ${If} ${FileExists} "$INSTDIR\cli\${CLI_EXE}"
    ${GetSize} "$INSTDIR\cli" "/S=0K" $3 $1 $2
    IntOp $0 $0 + $3
  ${EndIf}
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${REG_UNINSTALL}" "EstimatedSize" "$0"
  ${If} $Relaunch == "1"
    Exec '"$INSTDIR\app\${APP_EXE}"'
  ${EndIf}
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_APP} "$(SEC_APP_DESC)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CLI} "$(SEC_CLI_DESC)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} "$(SEC_DESKTOP_DESC)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function .onInit
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/WAITPID=" $R1
  ${IfNot} ${Errors}
    StrCpy $WaitPid $R1
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/RELAUNCH" $R1
  ${IfNot} ${Errors}
    StrCpy $Relaunch "1"
  ${EndIf}
  ; What an earlier setup recorded, read before this one writes anything.
  ReadRegStr $PreviousInstallDir HKCU "${REG_APP}" "InstallDir"
  ReadRegDWORD $PreviousCli HKCU "${REG_APP}" "CliInstalled"
  ; Upgrades, including silent in-app updates, keep the earlier choice of the command-line tool.
  ${If} $PreviousCli = 1
    !insertmacro SelectSection ${SEC_CLI}
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/CLI" $R1
  ${IfNot} ${Errors}
    !insertmacro SelectSection ${SEC_CLI}
  ${EndIf}
FunctionEnd

; A failed update must not leave the user without the app: start the version that is still installed, unless an
; earlier instance is still running.
Function .onInstFailed
  ${If} $Relaunch == "1"
  ${AndIf} ${FileExists} "$INSTDIR\app\${APP_EXE}"
    Call FilesInUse
    ${If} $R6 = 0
      Exec '"$INSTDIR\app\${APP_EXE}"'
    ${EndIf}
  ${EndIf}
FunctionEnd

; An upgrade installs into the existing folder, so the directory page is only shown for a first installation.
Function SkipDirectoryOnUpgrade
  ${If} $PreviousInstallDir != ""
  ${AndIf} ${FileExists} "$PreviousInstallDir\app\${APP_EXE}"
    Abort
  ${EndIf}
FunctionEnd

Function un.onInit
  ReadRegStr $PreviousInstallDir HKCU "${REG_APP}" "InstallDir"
  ReadRegDWORD $PreviousCli HKCU "${REG_APP}" "CliInstalled"
FunctionEnd

Section "Uninstall"
  Call un.EnsureAppClosed
  ${If} ${FileExists} "$INSTDIR\cli\*.*"
    Call un.CliIsOurs
    ${If} $R8 = 1
      !insertmacro REMOVE_CLI
    ${EndIf}
  ${EndIf}
  ${If} ${FileExists} "$INSTDIR\app\*.*"
    Call un.AppFolderIsOurs
    ${If} $R8 = 1
      RMDir /r "$INSTDIR\app"
    ${EndIf}
  ${EndIf}
  Delete "$INSTDIR\uninstall.exe"
  ; Removed only when empty; anything else in the folder stays.
  RMDir "$INSTDIR"
  ; Shortcuts and registry entries describe the registered installation. Uninstalling another copy leaves them.
  Call un.RegistryIsOurs
  ${If} $R8 = 1
    Delete "$SMPROGRAMS\${APP_NAME}.lnk"
    Delete "$DESKTOP\${APP_NAME}.lnk"
    ; The app's start-at-sign-in entry (ADR-014); Windows' own Startup apps switch for it is Windows' to keep.
    DeleteRegValue HKCU "${REG_RUN}" "${RUN_VALUE}"
    ; When another program kept a file in app\ locked, the marker may already be gone. The registry entries then
    ; stay, so the next setup still recognizes the folder as this program's and can replace it.
    ${IfNot} ${FileExists} "$INSTDIR\app\*.*"
      DeleteRegKey HKCU "${REG_UNINSTALL}"
      DeleteRegKey HKCU "${REG_APP}"
    ${EndIf}
  ${EndIf}
  ; User data in %LOCALAPPDATA%\FileMergeAssistant (recycle bin, cache, logs, results) is kept on purpose.
SectionEnd
