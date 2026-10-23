<#
.SYNOPSIS
    Signs Windows artifacts with a persistent identity and verifies the result.
.DESCRIPTION
    Production requires CERT_PATH/CERT_PASS or SIGNING_CERT_THUMBPRINT, a trusted
    certificate and a timestamp. Development signing is explicit and never adds trust.
#>
param(
    [string]$FilePath,
    [string]$CertificateOutputDirectory,
    [switch]$AllowDevelopmentCertificate,
    [switch]$CheckCertificateOnly,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Authenticode signing requires Windows.' }
$developmentSubject = 'CN=Bulk Switch Backup Pro (Yash Nalawde)'
if ($env:CERT_PATH) {
    if (-not (Test-Path -LiteralPath $env:CERT_PATH -PathType Leaf)) { throw 'CERT_PATH does not identify a certificate file.' }
    $password = if ($env:CERT_PASS) { ConvertTo-SecureString $env:CERT_PASS -AsPlainText -Force } else { [securestring]::new() }
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($env:CERT_PATH, $password)
} elseif ($env:SIGNING_CERT_THUMBPRINT) {
    $thumbprint = $env:SIGNING_CERT_THUMBPRINT -replace '\s', ''
    $cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -ErrorAction Stop
} elseif ($AllowDevelopmentCertificate) {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $developmentSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if (-not $cert) { throw 'No Bulk Switch Backup development signing certificate is installed.' }
} else {
    throw 'Public signing requires CERT_PATH/CERT_PASS or SIGNING_CERT_THUMBPRINT. Self-signed fallback is disabled.'
}
if (-not $cert.HasPrivateKey -or $cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -le (Get-Date)) {
    throw 'Signing certificate is expired, not yet valid, or has no private key.'
}
$usage = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
if (-not ($usage.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })) {
    throw 'Certificate is not enabled for code signing.'
}
$selfSigned = $cert.Subject -eq $cert.Issuer
if (-not $AllowDevelopmentCertificate -and ($selfSigned -or -not $cert.Verify())) {
    throw 'Public signing requires a trusted certificate chain; a self-signed identity cannot establish public SmartScreen reputation.'
}
if ($CheckCertificateOnly) { Write-Host "Signing identity ready; development certificate: $selfSigned"; return }
if (-not $FilePath -or -not (Test-Path -LiteralPath $FilePath -PathType Leaf)) { throw 'Signing target does not exist.' }
$resolved = (Resolve-Path -LiteralPath $FilePath).Path
try {
    Set-AuthenticodeSignature -LiteralPath $resolved -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampServer -ErrorAction Stop | Out-Null
} catch {
    if (-not ($AllowDevelopmentCertificate -and $selfSigned)) { throw }
    Write-Warning 'Timestamp unavailable for development signing; this artifact is for local testing only.'
    Set-AuthenticodeSignature -LiteralPath $resolved -Certificate $cert -HashAlgorithm SHA256 -ErrorAction Stop | Out-Null
}
$signature = Get-AuthenticodeSignature -LiteralPath $resolved
if ($signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw 'Signature signer does not match the configured certificate.' }
if ($AllowDevelopmentCertificate -and $selfSigned) {
    if ($signature.Status -notin @('Valid', 'NotTrusted', 'UnknownError')) { throw "Development signature failed: $($signature.Status)" }
    $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $null = $chain.Build($cert)
        if (@($chain.ChainStatus | Where-Object { $_.Status -ne 'UntrustedRoot' }).Count) { throw 'Development certificate chain has errors beyond its untrusted root.' }
    } finally { $chain.Dispose() }
    Write-Warning "Development signature only ($($signature.Status)); SmartScreen warnings on other PCs remain expected. No trust stores were changed."
} elseif ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) {
    throw "Public signing verification failed: $($signature.Status); a valid timestamped signature is required."
}
if ($CertificateOutputDirectory) {
    New-Item -ItemType Directory -Path $CertificateOutputDirectory -Force | Out-Null
    Export-Certificate -Cert $cert -FilePath (Join-Path $CertificateOutputDirectory 'BulkSwitchBackupPro_CodeSigning.cer') -Force | Out-Null
}
Write-Host "Signed $([IO.Path]::GetFileName($resolved)): $($signature.Status); timestamp: $($null -ne $signature.TimeStamperCertificate)"
