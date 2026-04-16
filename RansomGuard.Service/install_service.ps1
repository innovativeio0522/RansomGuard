sc.exe stop RansomGuardSentinel
Start-Sleep -Seconds 1
sc.exe delete RansomGuardSentinel
Start-Sleep -Seconds 2
sc.exe create RansomGuardSentinel binPath= "f:\Github Projects\RansomGuard\RansomGuard.Service\publish\RansomGuard.Service.exe" start= auto DisplayName= "RansomGuard Sentinel Service"
sc.exe description RansomGuardSentinel "Provides proactive ransomware protection and recovery shields."
sc.exe start RansomGuardSentinel
