
!include "MUI2.nsh"

Name "SocialVPN 0.5.1"
Outfile "socialvpn_0.5.1.exe"
InstallDir "$PROGRAMFILES\SocialVPN"

RequestExecutionLevel admin

!define MUI_ABORTWARNING

!insertmacro MUI_PAGE_LICENSE "LICENSE.txt"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_RUN "$WINDIR\explorer.exe"
!define MUI_FINISHPAGE_RUN_PARAMETERS "http://127.0.0.1:58888/"
!define MUI_FINISHPAGE_RUN_TEXT "Open SocialVPN Manager"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Virtual Network Interface Install" SecTap

  SetOutPath "$INSTDIR\driver"

  ; Check if we are running on a 64 bit system.
  System::Call "kernel32::GetCurrentProcess() i .s"
  System::Call "kernel32::IsWow64Process(i s, *i .r0)"
  IntCmp $0 0 tap-32bit

; tap-64bit:

  DetailPrint "We are running on a 64-bit system."

  File "drivers\windows_tap\Windows_64\driverhelper.exe"
  File "drivers\windows_tap\Windows_64\IpopTap.inf"
  File "drivers\windows_tap\Windows_64\IpopTap.cat"
  File "drivers\windows_tap\Windows_64\IpopTap.sys"

goto tapend

tap-32bit:

  DetailPrint "We are running on a 32-bit system."

  File "drivers\windows_tap\Windows_32\driverhelper.exe"
  File "drivers\windows_tap\Windows_32\IpopTap.inf"
  File "drivers\windows_tap\Windows_32\IpopTap.cat"
  File "drivers\windows_tap\Windows_32\IpopTap.sys"

tapend:

  nsExec::ExecToLog '"$INSTDIR\driver\driverhelper.exe" remove IpopTap'

  nsExec::ExecToLog '"$INSTDIR\driver\driverhelper.exe" install \
  "$INSTDIR\driver\IpopTap.inf" IpopTap'

  ; get PnpInstanceID necessary to lookup Guid for registry
  nsExec::ExecToStack '"$INSTDIR\driver\driverhelper.exe" status IpopTap'
  Pop $R0
  Pop $R1
  StrCpy $R0 $R1 13
  DetailPrint "PnpInstanceID for tap driver : $R0"

  ; loop through guids until we find matching with PnpInstanceID then rename
  StrCpy $0 0
loop:
  EnumRegKey $1 HKLM "SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Adapters" $0
  StrCmp $1 "" done
  IntOp $0 $0 + 1
  StrCpy $2 "SYSTEM\CurrentControlSet\Control\Network\{4D36E972-E325-11CE-BFC1-08002BE10318}\$1\Connection"
  ReadRegStr $3 HKLM $2 "PnpInstanceID"
  DetailPrint "PnpInstanceID = $3"
  StrCmp $3 $R0 nametap loop
nametap:
  WriteRegStr HKLM "$2" "Name" "tapipop"
done:

SectionEnd

Section "SocialVPN Install" SecSocialVPN

  SetOutPath $INSTDIR

  nsExec::ExecToLog '"$SYSDIR\net.exe" stop SocialVPN'

  File "brunet.config"
  File "Brunet.dll"
  File "Brunet.Security.dll"
  File "Brunet.Services.Coordinate.dll"
  File "Brunet.Services.Dht.dll"
  File "Brunet.Services.XmlRpc.dll"
  File "Brunet.Xmpp.dll"
  File "Changelog.txt"
  File "CookComputing.XmlRpcV2.dll"
  File "ipop.config"
  File "Ipop.Managed.dll"
  File "jabber-net.dll"
  File "jquery.js"
  File "jquery-ui.css"
  File "jquery-ui.js"
  File "LICENSE.txt"
  File "ManagedOpenSsl.dll"
  File "Mono.Security.dll"
  File "NDesk.Options.dll"
  File "README.txt"
  File "start_socialvpn.cmd"
  File "stop_socialvpn.cmd"
  File "socialvpn_manager.html"
  File "socialdns.css"
  File "socialdns.html"
  File "socialdns.js"
  File "socialvpn.css"
  File "SocialVPN.exe"
  File "SocialVPN.exe.config"
  File "socialvpn.html"
  File "socialvpn.js"
  File "SocialVPNService.exe"
  File "SocialVPNService.exe.config"
  File "zlib.net.dll"

  nsExec::ExecToLog '"$WINDIR\Microsoft.NET\Framework\v2.0.50727\installutil.exe" /uninstall "$INSTDIR\SocialVPNService.exe"'
  nsExec::ExecToLog '"$WINDIR\Microsoft.NET\Framework\v2.0.50727\installutil.exe" "$INSTDIR\SocialVPNService.exe"'
  nsExec::ExecToLog '"$SYSDIR\net.exe" start SocialVPN'

SectionEnd

Section -post SecPost

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  SetOutPath "$SMPROGRAMS"
  CreateDirectory "$SMPROGRAMS\SocialVPN"
  CreateShortCut "$SMPROGRAMS\SocialVPN\Start SocialVPN (run as admin).lnk" "$INSTDIR\start_socialvpn.cmd"
  CreateShortCut "$SMPROGRAMS\SocialVPN\Stop SocialVPN (run as admin).lnk" "$INSTDIR\stop_socialvpn.cmd"
  CreateShortCut "$SMPROGRAMS\SocialVPN\SocialVPN Manager.lnk" "$INSTDIR\socialvpn_manager.html"
  CreateShortCut "$SMPROGRAMS\SocialVPN\Uninstall SocialVPN.lnk" "$INSTDIR\Uninstall.exe"

  CreateShortCut "$DESKTOP\Start SocialVPN (run as admin).lnk" "$INSTDIR\start_socialvpn.cmd"
  CreateShortCut "$DESKTOP\Stop SocialVPN (run as admin).lnk" "$INSTDIR\stop_socialvpn.cmd"
  CreateShortCut "$DESKTOP\SocialVPN Manager.lnk" "$INSTDIR\socialvpn_manager.html"

  WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$(^Name)" DisplayName "$(^Name)"
  WriteRegStr HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$(^Name)" UninstallString $INSTDIR\Uninstall.exe
  WriteRegDWORD HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$(^Name)" NoModify 1
  WriteRegDWORD HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$(^Name)" NoRepair 1

SectionEnd

Section "Uninstall"

  nsExec::ExecToLog '"$SYSDIR\net.exe" stop SocialVPN'
  nsExec::ExecToLog '"$WINDIR\Microsoft.NET\Framework\v2.0.50727\installutil.exe" /uninstall "$INSTDIR\SocialVPNService.exe"'
  nsExec::ExecToLog '"$INSTDIR\driver\driverhelper.exe" remove IpopTap'

  Delete "$INSTDIR\driver\driverhelper.exe"
  Delete "$INSTDIR\driver\IpopTap.inf"
  Delete "$INSTDIR\driver\IpopTap.cat"
  Delete "$INSTDIR\driver\IpopTap.sys"
  RMDir "$INSTDIR\driver"

  Delete "$INSTDIR\Uninstall.exe"

  Delete "$INSTDIR\brunet.config"
  Delete "$INSTDIR\Brunet.dll"
  Delete "$INSTDIR\Brunet.Security.dll"
  Delete "$INSTDIR\Brunet.Services.Coordinate.dll"
  Delete "$INSTDIR\Brunet.Services.Dht.dll"
  Delete "$INSTDIR\Brunet.Services.XmlRpc.dll"
  Delete "$INSTDIR\Brunet.Xmpp.dll"
  Delete "$INSTDIR\Changelog.txt"
  Delete "$INSTDIR\CookComputing.XmlRpcV2.dll"
  Delete "$INSTDIR\ipop.config"
  Delete "$INSTDIR\Ipop.Managed.dll"
  Delete "$INSTDIR\jabber-net.dll"
  Delete "$INSTDIR\jquery.js"
  Delete "$INSTDIR\jquery-ui.css"
  Delete "$INSTDIR\jquery-ui.js"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\ManagedOpenSsl.dll"
  Delete "$INSTDIR\Mono.Security.dll"
  Delete "$INSTDIR\NDesk.Options.dll"
  Delete "$INSTDIR\README.txt"
  Delete "$INSTDIR\start_socialvpn.cmd"
  Delete "$INSTDIR\stop_socialvpn.cmd"
  Delete "$INSTDIR\socialvpn_manager.html"
  Delete "$INSTDIR\socialdns.css"
  Delete "$INSTDIR\socialdns.html"
  Delete "$INSTDIR\socialdns.js"
  Delete "$INSTDIR\socialvpn.css"
  Delete "$INSTDIR\SocialVPN.exe"
  Delete "$INSTDIR\SocialVPN.exe.config"
  Delete "$INSTDIR\socialvpn.html"
  Delete "$INSTDIR\socialvpn.js"
  Delete "$INSTDIR\SocialVPNService.exe"
  Delete "$INSTDIR\SocialVPNService.exe.config"
  Delete "$INSTDIR\zlib.net.dll"
  Delete "$INSTDIR\state.xml"
  Delete "$INSTDIR\sdnsstate.xml"
  Delete "$INSTDIR\private_key"
  Delete "$INSTDIR\social.config"
  Delete "$INSTDIR\Brunet.log"
  Delete "$INSTDIR\InstallUtil.InstallLog"
  Delete "$INSTDIR\SocialVPNService.InstallLog"

  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\SocialVPN\Start SocialVPN (run as admin).lnk"
  Delete "$SMPROGRAMS\SocialVPN\Stop SocialVPN (run as admin).lnk"
  Delete "$SMPROGRAMS\SocialVPN\SocialVPN Manager.lnk"
  Delete "$SMPROGRAMS\SocialVPN\Uninstall SocialVPN.lnk"
  RMDir "$SMPROGRAMS\SocialVPN"

  Delete "$DESKTOP\Start SocialVPN (run as admin).lnk"
  Delete "$DESKTOP\Stop SocialVPN (run as admin).lnk"
  Delete "$DESKTOP\SocialVPN Manager.lnk"

  DeleteRegKey HKLM "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$(^Name)"

SectionEnd

