#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$CertificateThumbprint
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskInput=[IO.Path]::GetFullPath($Installer)
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $taskInput -PathType Leaf)) { throw 'Installer file does not exist.' }
if ([IO.Path]::GetExtension($taskInput) -ne '.exe') { throw 'Expected an installer executable.' }
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a new output directory; the original installer is preserved.' }
$taskInputSignature=Get-AuthenticodeSignature -LiteralPath $taskInput
if ($taskInputSignature.Status -ne 'NotSigned') { throw 'Expected the original unsigned installer.' }
$taskKeyFolder=Join-Path $taskRoot '.tools\signing'
$taskKeyRecord=Join-Path $taskKeyFolder 'self-signing.json'
if (-not $CertificateThumbprint -and (Test-Path -LiteralPath $taskKeyRecord)) {
    $CertificateThumbprint=(Get-Content -LiteralPath $taskKeyRecord -Raw | ConvertFrom-Json).thumbprint
}
if ($CertificateThumbprint) {
    if ($CertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$') { throw 'Invalid certificate thumbprint.' }
    $taskCertificate=Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
} else {
    $taskCertificate=New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Syphon Local Development' -FriendlyName 'Syphon local self-signing (not public trust)' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(2)
    New-Item -ItemType Directory -Force -Path $taskKeyFolder | Out-Null
    @{thumbprint=$taskCertificate.Thumbprint;subject=$taskCertificate.Subject;notAfter=$taskCertificate.NotAfter.ToUniversalTime().ToString('o');privateKey='Non-exportable Windows current-user certificate store; never in repository';trust='Not installed in Root or TrustedPublisher'} | ConvertTo-Json | Set-Content -LiteralPath $taskKeyRecord -Encoding UTF8
}
if (-not $taskCertificate.HasPrivateKey -or $taskCertificate.NotAfter -le (Get-Date) -or $taskCertificate.NotBefore -gt (Get-Date)) { throw 'Certificate needs a usable, current private key.' }
if ($taskCertificate.Subject -ne $taskCertificate.Issuer -or -not ($taskCertificate.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')) { throw 'Expected a self-signed code-signing certificate.' }
$taskOriginalHash=(Get-FileHash -LiteralPath $taskInput -Algorithm SHA256).Hash.ToLowerInvariant()
New-Item -ItemType Directory -Path $taskOutput | Out-Null
$taskSigned=Join-Path $taskOutput ([IO.Path]::GetFileName($taskInput))
Copy-Item -LiteralPath $taskInput -Destination $taskSigned
Set-AuthenticodeSignature -LiteralPath $taskSigned -Certificate $taskCertificate -HashAlgorithm SHA256 | Out-Null
$taskSignature=Get-AuthenticodeSignature -LiteralPath $taskSigned
# NotTrusted is expected: this workflow deliberately installs no trust anchors.
if ($taskSignature.Status -notin @('Valid','NotTrusted','UnknownError') -or $taskSignature.SignerCertificate.Thumbprint -ne $taskCertificate.Thumbprint) { throw "Signing failed: $($taskSignature.Status): $($taskSignature.StatusMessage)" }
$taskCrypto=(& (Join-Path $PSScriptRoot 'verify-self-signature.ps1') -File $taskSigned -Thumbprint $taskCertificate.Thumbprint | ConvertFrom-Json)
if (-not $taskCrypto.cryptographicSignatureValid -or -not $taskCrypto.fileDigestValid) { throw 'Cryptographic signature verification failed.' }
if ((Get-FileHash -LiteralPath $taskInput -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskOriginalHash) { throw 'Original input changed during signing.' }
Export-Certificate -Cert $taskCertificate -FilePath (Join-Path $taskOutput 'Syphon-Local-Development.cer') | Out-Null
$taskHash=(Get-FileHash -LiteralPath $taskSigned -Algorithm SHA256).Hash.ToLowerInvariant()
$taskRecord=[ordered]@{installer=$taskSigned;bytes=(Get-Item -LiteralPath $taskSigned).Length;sha256=$taskHash;originalSha256=$taskOriginalHash;signed=$true;signatureType='self-signed Authenticode';windowsTrustStatus=[string]$taskSignature.Status;windowsTrustMessage=$taskSignature.StatusMessage;cryptographicSignatureValid=$true;fileDigestValid=$true;certificateThumbprint=$taskCertificate.Thumbprint;certificateSubject=$taskCertificate.Subject;certificateExpiresUtc=$taskCertificate.NotAfter.ToUniversalTime().ToString('o');timestamped=$false;publiclyTrusted=$false;scope='Outer installer only; embedded files retain their existing signatures';privateKeyExported=$false}
$taskRecord | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'self-sign-summary.json') -Encoding UTF8
"$taskHash  $([IO.Path]::GetFileName($taskSigned))" | Set-Content -LiteralPath (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding ASCII
'Self-signed local installer. Windows publisher trust and SmartScreen warnings may remain. The .cer is public only; the private key remains in the Windows current-user store. No certificate was added to trust stores. No timestamp was requested. Embedded app and uninstaller signatures are unchanged.' | Set-Content -LiteralPath (Join-Path $taskOutput 'SELF-SIGNING.txt') -Encoding UTF8
$taskRecord | ConvertTo-Json
