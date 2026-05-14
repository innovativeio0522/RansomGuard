$pfxPath = "RansomGuard.Package\RansomGuard_TemporaryKey.pfx"
if ([string]::IsNullOrWhiteSpace($env:RANSOMGUARD_CERT_PASSWORD)) {
    throw "Set RANSOMGUARD_CERT_PASSWORD before importing the development signing certificate."
}
$password = ConvertTo-SecureString -String $env:RANSOMGUARD_CERT_PASSWORD -Force -AsPlainText

Write-Host "Importing certificate to Trusted Root..." -ForegroundColor Cyan
Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\Root -Password $password

Write-Host "Importing certificate to Trusted People..." -ForegroundColor Cyan
Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $password

Write-Host "Done! You can now install the MSIX bundle." -ForegroundColor Green
