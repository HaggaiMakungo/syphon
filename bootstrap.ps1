$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$taskTools = Join-Path $taskRoot '.tools'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskExisting = Join-Path $taskTools 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $taskExisting) {
    $taskInstalled = & $taskExisting --version
    if ($LASTEXITCODE -eq 0 -and $taskInstalled -eq $taskVersion) {
        Write-Output "Project-local SDK $taskVersion is ready."
        return
    }
}
$taskMetadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 60
$taskArchive = $taskMetadata.releases | Where-Object { $_.sdk.version -eq $taskVersion } |
    Select-Object -First 1 -ExpandProperty sdk | Select-Object -ExpandProperty files |
    Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' }
if (-not $taskArchive -or -not $taskArchive.url.StartsWith('https://builds.dotnet.microsoft.com/dotnet/Sdk/') -or $taskArchive.hash -notmatch '^[a-fA-F0-9]{128}$') {
    throw 'Pinned SDK archive was not found in the official release manifest.'
}
$taskZip = Join-Path $taskTools 'dotnet-sdk.zip'
if (-not (Test-Path -LiteralPath $taskZip) -or (Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash -ne $taskArchive.hash) {
    # Use Microsoft's blob origin to avoid a stalled intermediary CDN transfer.
    # The archive must still match the official release manifest's SHA-512.
    $taskDownload = $taskArchive.url.Replace('https://builds.dotnet.microsoft.com/dotnet/', 'https://dotnetcli.blob.core.windows.net/dotnet/')
    & curl.exe --fail --silent --show-error --connect-timeout 15 --max-time 600 --speed-time 30 --speed-limit 1024 --retry 2 --output $taskZip $taskDownload
    if ($LASTEXITCODE -ne 0) { throw 'SDK archive download failed.' }
}
if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash -ne $taskArchive.hash) { throw 'SDK archive checksum mismatch.' }
Expand-Archive -LiteralPath $taskZip -DestinationPath (Join-Path $taskTools 'dotnet') -Force
if (-not (Test-Path -LiteralPath (Join-Path $taskTools 'dotnet\dotnet.exe'))) { throw 'SDK installation did not finish.' }
