[CmdletBinding()]
param([string]$OutputDirectory, [string]$Version='0.1.0-beta.2')
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Use a valid release version.' }
$taskRoot=Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory=Join-Path $taskRoot "artifacts\installer-$Version" }
$taskDotnet=Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
Push-Location $taskRoot
try {
    & node ui/desktop/build.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    foreach ($taskProject in @('Desktop','NativeHost')) {
        $taskSubfolder=if ($taskProject -eq 'Desktop') {'app'} else {'host'}
        & $taskDotnet publish "src/Syphon.$taskProject/Syphon.$taskProject.csproj" -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.win-x64.lock.json "-p:Version=$Version" -p:DebugType=None -o "artifacts/setup-stage/$taskSubfolder" --nologo
        if ($LASTEXITCODE -ne 0) { throw "$taskProject publish failed." }
    }
    & $taskDotnet publish src/Syphon.Setup/Syphon.Setup.csproj -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.win-x64.lock.json "-p:Version=$Version" -p:DebugType=None -o artifacts/setup-core --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
    New-Item -ItemType Directory -Force artifacts/setup-stage/prerequisites | Out-Null
    $taskBootstrap=Join-Path $taskRoot 'artifacts\setup-stage\prerequisites\MicrosoftEdgeWebview2Setup.exe'
    if (-not (Test-Path -LiteralPath $taskBootstrap)) {
        & curl.exe --fail --location --silent --show-error --retry 2 --connect-timeout 20 --max-time 180 --output $taskBootstrap --url 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
        if ($LASTEXITCODE -ne 0) { throw 'Microsoft WebView2 download failed.' }
    }
    $taskSignature=Get-AuthenticodeSignature -LiteralPath $taskBootstrap
    if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'WebView2 bootstrapper must have a valid Microsoft signature.' }
    & python packaging/prepare-stage.py --version $Version
    if ($LASTEXITCODE -ne 0) { throw 'Dependency staging failed.' }
    & python packaging/assemble-installer.py --output $OutputDirectory --version $Version
    if ($LASTEXITCODE -ne 0) { throw 'Installer assembly failed.' }
    & $taskDotnet src/Syphon.Setup/bin/Release/net10.0-windows10.0.19041.0/win-x64/Syphon.Setup.dll --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Installer engine checks failed.' }
} finally { Pop-Location }
