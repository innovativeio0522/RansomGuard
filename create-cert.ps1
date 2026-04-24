$publisher = "CN=RansomGuard"
$pfxPath = "RansomGuard.Package\RansomGuard_TemporaryKey.pfx"
$password = ConvertTo-SecureString -String "Password123" -Force -AsPlainText

Write-Host "Creating self-signed certificate..." -ForegroundColor Cyan

$cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
    -KeyUsage DigitalSignature -FriendlyName "RansomGuard Dev Cert" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password

Write-Host "Certificate created at: $pfxPath" -ForegroundColor Green
Write-Host "To trust this certificate, run: " -ForegroundColor Yellow
Write-Host "Import-Certificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor White
