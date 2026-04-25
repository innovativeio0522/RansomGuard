$pfxPath = "RansomGuard.Package\RansomGuard_TemporaryKey.pfx"
$password = ConvertTo-SecureString -String "Password123" -Force -AsPlainText

Write-Host "Importing certificate to Trusted Root..." -ForegroundColor Cyan
Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\Root -Password $password

Write-Host "Importing certificate to Trusted People..." -ForegroundColor Cyan
Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $password

Write-Host "Done! You can now install the MSIX bundle." -ForegroundColor Green
