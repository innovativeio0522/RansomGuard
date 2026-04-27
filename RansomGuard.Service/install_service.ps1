sc.exe stop RGService
Start-Sleep -Seconds 1
sc.exe delete RGService
Start-Sleep -Seconds 2
sc.exe create RGService binPath= "f:\Github Projects\RansomGuard\RansomGuard.Service\publish\RGService.exe" start= auto DisplayName= "RG Sentinel Service"
sc.exe description RGService "Provides proactive ransomware protection and recovery shields."
sc.exe start RGService
