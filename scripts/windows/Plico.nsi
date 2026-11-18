Unicode true
!include "MUI2.nsh"
!include "x64.nsh"

!define NAME "Plico"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Plico"
!define PDF_PROGID "Plico.PDF"
!define PDF_OPEN_WITH "Software\Classes\.pdf\OpenWithProgids"
!define PDF_APPLICATION "Software\Classes\${PDF_PROGID}"

Name "${NAME}"
OutFile "${OUTPUT}\Plico-${VERSION}-${RUNTIME}-setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Plico"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel user
ManifestDPIAware true
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${NAME}"
VIAddVersionKey "FileDescription" "Plico setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Plico contributors"
SetCompressor /SOLID lzma

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

!if "${RUNTIME}" == "win-arm64"
Function .onInit
    ${IfNot} ${IsNativeARM64}
        MessageBox MB_OK|MB_ICONSTOP "This Plico setup requires Windows on ARM64." /SD IDOK
        SetErrorLevel 1
        Quit
    ${EndIf}
FunctionEnd
!endif

Section "Install"
    SetShellVarContext current
    SetRegView 64
    SetOutPath "$INSTDIR"
    File /r "${PAYLOAD}\*"
    WriteUninstaller "$INSTDIR\Uninstall-Plico.exe"
    CreateShortCut "$SMPROGRAMS\Plico.lnk" "$INSTDIR\Plico.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${NAME}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Plico.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall-Plico.exe"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall-Plico.exe" /S'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
    WriteRegStr HKCU "${PDF_APPLICATION}" "" "Plico PDF document"
    WriteRegStr HKCU "${PDF_APPLICATION}\DefaultIcon" "" '"$INSTDIR\Plico.exe",0'
    WriteRegStr HKCU "${PDF_APPLICATION}\shell\open\command" "" '"$INSTDIR\Plico.exe" "%1"'
    WriteRegStr HKCU "${PDF_OPEN_WITH}" "${PDF_PROGID}" ""
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Section "Uninstall"
    SetShellVarContext current
    SetRegView 64
    !include "${UNINSTALL_MANIFEST}"
    Delete "$SMPROGRAMS\Plico.lnk"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    DeleteRegValue HKCU "${PDF_OPEN_WITH}" "${PDF_PROGID}"
    DeleteRegKey HKCU "${PDF_APPLICATION}"
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
    Delete "$INSTDIR\Uninstall-Plico.exe"
    RMDir "$INSTDIR"
SectionEnd
