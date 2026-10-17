#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$taskFolder = Join-Path $PSScriptRoot '.tools\media'
New-Item -ItemType Directory -Path $taskFolder -Force | Out-Null
$taskHeaders = @{ 'User-Agent' = 'Syphon-development-setup'; 'Accept' = 'application/vnd.github+json' }
$taskRelease = Invoke-RestMethod -Uri 'https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest' -Headers $taskHeaders
$taskBinary = $taskRelease.assets | Where-Object { $_.name -eq 'yt-dlp.exe' } | Select-Object -First 1
$taskChecksums = $taskRelease.assets | Where-Object { $_.name -eq 'SHA2-256SUMS' } | Select-Object -First 1
if (-not $taskBinary -or -not $taskChecksums) { throw 'The official release is missing its Windows binary or checksums.' }
foreach ($taskAsset in @($taskBinary, $taskChecksums)) {
    $taskAssetUri = [Uri]$taskAsset.browser_download_url
    if ($taskAssetUri.Scheme -ne 'https' -or $taskAssetUri.Host -ne 'github.com' -or -not $taskAssetUri.AbsolutePath.StartsWith('/yt-dlp/yt-dlp/releases/download/')) { throw 'Unexpected release download origin.' }
}
$taskPending = Join-Path $taskFolder 'yt-dlp.exe.pending'
try {
    & curl.exe --fail --location --silent --show-error --retry 2 --connect-timeout 20 --max-time 180 --output $taskPending --url $taskBinary.browser_download_url
    if ($LASTEXITCODE -ne 0) { throw 'The official yt-dlp download failed. Try running this installer again.' }
    $taskHashes = (Invoke-WebRequest -UseBasicParsing -Uri $taskChecksums.browser_download_url -Headers $taskHeaders).Content
    if ($taskHashes -is [byte[]]) { $taskHashes = [Text.Encoding]::UTF8.GetString($taskHashes) }
    $taskExpected = [regex]::Match([string]$taskHashes, '(?im)^([a-f0-9]{64})\s+\*?yt-dlp\.exe\s*$').Groups[1].Value
    if ($taskExpected.Length -ne 64 -or (Get-FileHash -LiteralPath $taskPending -Algorithm SHA256).Hash -ne $taskExpected) { throw 'yt-dlp checksum verification failed.' }
    Move-Item -LiteralPath $taskPending -Destination (Join-Path $taskFolder 'yt-dlp.exe') -Force
    $taskRecord = @{ release = $taskRelease.tag_name; sha256 = $taskExpected.ToLowerInvariant(); source = $taskBinary.browser_download_url }
    [IO.File]::WriteAllText((Join-Path $taskFolder 'yt-dlp-install.json'), ($taskRecord | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Write-Output "Installed verified yt-dlp $($taskRelease.tag_name) in Syphon's local tools folder."
} finally { if (Test-Path -LiteralPath $taskPending) { Remove-Item -LiteralPath $taskPending } }
