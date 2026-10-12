[Console]::OutputEncoding=[Text.Encoding]::UTF8
taskkill /IM Unictrl.exe /F 2>&1 | Out-Null
Start-Sleep 1
$json = @'
{
  "Role": "sender",
  "MacAddress": "10.211.55.2",
  "Port": 24860,
  "PairingKey": "dGVzdC1rZXktMTIz",
  "DeviceId": "A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D",
  "MacId": "A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D",
  "MacName": "MacTest",
  "MacSide": "right",
  "MacPointerScale": 1.0,
  "Diagnostics": true,
  "Enabled": true,
  "StartAtLogin": false
}
'@
$json | Out-File "C:\Users\admin\AppData\Local\Unictrl\settings.json" -Encoding utf8
Start-ScheduledTask -TaskName "UnictrlT3"
Start-Sleep 8
Write-Host "alive:" ($null -ne (Get-Process Unictrl -EA SilentlyContinue))
Get-Content "C:\Users\admin\AppData\Local\Unictrl\unictrl.log" -Tail 2 | Out-String
