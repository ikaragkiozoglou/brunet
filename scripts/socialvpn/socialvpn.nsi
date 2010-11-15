
!include "MUI2.nsh"

Name "SocialVPN 0.5.0"
Outfile "SocialVPN_0.5.0.exe"
InstallDir "$LOCALAPPDATA\SocialVPN"

RequestExecutionLevel admin

!define MUI_ABORTWARNING

!insertmacro MUI_PAGE_LICENSE "LICENSE.txt"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Tap Driver Install" SecTap

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

SectionEnd

Section "SocialVPN Service Install" SecService

  nsExec::ExecToLog '"$SYSDIR\net.exe" stop SocialVPN'
  nsExec::ExecToLog '"$WINDIR\Microsoft.NET\Framework\v2.0.50727\installutil.exe" /uninstall $INSTDIR\SocialVPNService.exe'
  nsExec::ExecToLog '"$WINDIR\Microsoft.NET\Framework\v2.0.50727\installutil.exe" $INSTDIR\SocialVPNService.exe'

SectionEnd

Section -post SecPost

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  SetOutPath "$SMPROGRAMS"
  CreateDirectory "$SMPROGRAMS\SocialVPN"
  CreateShortCut "$SMPROGRAMS\SocialVPN\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortCut "$SMPROGRAMS\SocialVPN\Start SocialVPN.lnk (Run as admin)" "$INSTDIR\start_socialvpn.cmd"
  CreateShortCut "$SMPROGRAMS\SocialVPN\Stop SocialVPN.lnk (Run as admin)" "$INSTDIR\stop_socialvpn.cmd"

SectionEnd

Section "Uninstall"

  nsExec::ExecToLog '"$SYSDIR\net.exe" stop SocialVPN'
  nsExec::ExecToLog '"$WINDIR\Microsoft.NET\Framework\v2.0.50727\installutil.exe" /uninstall $INSTDIR\SocialVPNService.exe'
  nsExec::ExecToLog '"$INSTDIR\driver\driverhelper.exe" remove IpopTap'

  Delete "$INSTDIR\driver\driverhelper.exe"
  Delete "$INSTDIR\driver\IpopTap.inf"
  Delete "$INSTDIR\driver\IpopTap.cat"
  Delete "$INSTDIR\driver\IpopTap.sys"

  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR\driver"

  Delete "brunet.config"
  Delete "Brunet.dll"
  Delete "Brunet.Security.dll"
  Delete "Brunet.Services.Coordinate.dll"
  Delete "Brunet.Services.Dht.dll"
  Delete "Brunet.Services.XmlRpc.dll"
  Delete "Brunet.Xmpp.dll"
  Delete "Changelog.txt"
  Delete "CookComputing.XmlRpcV2.dll"
  Delete "ipop.config"
  Delete "Ipop.Managed.dll"
  Delete "jabber-net.dll"
  Delete "jquery.js"
  Delete "jquery-ui.css"
  Delete "jquery-ui.js"
  Delete "LICENSE.txt"
  Delete "ManagedOpenSsl.dll"
  Delete "Mono.Security.dll"
  Delete "NDesk.Options.dll"
  Delete "README.txt"
  Delete "socialdns.css"
  Delete "socialdns.html"
  Delete "socialdns.js"
  Delete "socialvpn.css"
  Delete "SocialVPN.exe"
  Delete "SocialVPN.exe.config"
  Delete "socialvpn.html"
  Delete "socialvpn.js"
  Delete "SocialVPNService.exe"
  Delete "SocialVPNService.exe.config"
  Delete "zlib.net.dll"

  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\SocialVPN\Uninstall.lnk"
  RMDir "$SMPROGRAMS\SocialVPN"

SectionEnd

