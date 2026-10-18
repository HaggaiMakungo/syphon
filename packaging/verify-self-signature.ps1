#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$File,[Parameter(Mandatory=$true)][string]$Thumbprint)
$ErrorActionPreference='Stop'
$taskBytes=[IO.File]::ReadAllBytes([IO.Path]::GetFullPath($File))
$taskPe=[BitConverter]::ToInt32($taskBytes,0x3c)
if ($taskPe -le 0 -or $taskPe+256 -ge $taskBytes.Length -or [BitConverter]::ToUInt32($taskBytes,$taskPe) -ne 0x4550) { throw 'Invalid PE header.' }
$taskOptional=$taskPe+24
$taskKind=[BitConverter]::ToUInt16($taskBytes,$taskOptional)
if ($taskKind -notin @(0x10b,0x20b)) { throw 'Invalid PE optional header.' }
$taskSecurity=$taskOptional+$(if($taskKind -eq 0x20b){112}else{96})+32
$taskChecksum=$taskOptional+64
$taskCertificate=[BitConverter]::ToUInt32($taskBytes,$taskSecurity)
$taskCertificateSize=[BitConverter]::ToUInt32($taskBytes,$taskSecurity+4)
if ($taskCertificate -le $taskSecurity+8 -or $taskCertificateSize -lt 8 -or [long]$taskCertificate+$taskCertificateSize -ne $taskBytes.LongLength) { throw 'Invalid certificate table boundary.' }
$taskWinLength=[BitConverter]::ToUInt32($taskBytes,$taskCertificate)
if ($taskWinLength -lt 8 -or $taskWinLength -gt $taskCertificateSize -or [BitConverter]::ToUInt16($taskBytes,$taskCertificate+4) -ne 0x200 -or [BitConverter]::ToUInt16($taskBytes,$taskCertificate+6) -ne 2) { throw 'Invalid Authenticode certificate entry.' }
$taskCmsBytes=[byte[]]::new($taskWinLength-8)
[Array]::Copy($taskBytes,[long]$taskCertificate+8,$taskCmsBytes,0,$taskCmsBytes.Length)
$taskCms=[Security.Cryptography.Pkcs.SignedCms]::new()
$taskCms.Decode($taskCmsBytes)
$taskCms.CheckSignature($true)
if ($taskCms.SignerInfos.Count -ne 1 -or $taskCms.SignerInfos[0].Certificate.Thumbprint -ne $Thumbprint) { throw 'Unexpected Authenticode signer.' }
if ($taskCms.ContentInfo.ContentType.Value -ne '1.3.6.1.4.1.311.2.1.4') { throw 'Unexpected signed content type.' }
$taskReader=[System.Formats.Asn1.AsnReader]::new([ReadOnlyMemory[byte]]$taskCms.ContentInfo.Content,[System.Formats.Asn1.AsnEncodingRules]::BER)
$taskSequence=$taskReader.ReadSequence()
$null=$taskSequence.ReadEncodedValue()
$taskDigestInfo=$taskSequence.ReadSequence()
$taskAlgorithm=$taskDigestInfo.ReadSequence()
if ($taskAlgorithm.ReadObjectIdentifier() -ne '2.16.840.1.101.3.4.2.1') { throw 'Expected SHA-256 file digest.' }
$taskExpected=$taskDigestInfo.ReadOctetString()
# This installer PE has contiguous raw sections followed by the appended payload.
# Authenticode excludes checksum, security-directory entry and certificate table.
$taskHasher=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
try {
 $taskHasher.AppendData($taskBytes,0,$taskChecksum)
 $taskHasher.AppendData($taskBytes,$taskChecksum+4,$taskSecurity-$taskChecksum-4)
 $taskHasher.AppendData($taskBytes,$taskSecurity+8,$taskCertificate-$taskSecurity-8)
 $taskActual=$taskHasher.GetHashAndReset()
} finally { $taskHasher.Dispose() }
if ([Convert]::ToHexString($taskActual) -ne [Convert]::ToHexString($taskExpected)) { throw 'Authenticode file digest mismatch.' }
[ordered]@{cryptographicSignatureValid=$true;fileDigestValid=$true;signerThumbprint=$Thumbprint;trustValidated=$false;scope='Cryptographic verification only; no trusted roots installed or inferred'} | ConvertTo-Json
