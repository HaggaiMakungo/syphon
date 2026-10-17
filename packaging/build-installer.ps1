[CmdletBinding()]
param([string]$OutputDirectory, [string]$Version='0.1.0-beta.8', [string]$DependencyRoot)
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Use a valid release version.' }
$taskRoot=Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory=Join-Path $taskRoot "artifacts\installer-$Version" }
if (-not $DependencyRoot) { $DependencyRoot=Join-Path $taskRoot '.tools\release-deps\win-x64' }
$taskDotnet=Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet=(Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $taskRoot
try {
    $DependencyRoot=[IO.Path]::GetFullPath($DependencyRoot)
    $OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
    & python packaging/dependencies.py verify --root $DependencyRoot
    if ($LASTEXITCODE -ne 0) { throw 'Import the pinned release dependencies before building. See docs/packaging-inputs.md.' }
    $taskBootstrap=Join-Path $DependencyRoot 'MicrosoftEdgeWebview2Setup.exe'
    $taskSignature=Get-AuthenticodeSignature -LiteralPath $taskBootstrap
    if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'WebView2 bootstrapper must have a valid Microsoft signature.' }
    if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'ui/charts/node_modules/esbuild/lib/main.js'))) { throw 'Prepare the frontend dependencies first: npm ci --prefix ui/charts' }
    & python tests/packaging_test.py
    if ($LASTEXITCODE -ne 0) { throw 'Packaging regression checks failed.' }
    $taskWork=Join-Path $taskRoot ('artifacts\installer-work\'+[Guid]::NewGuid().ToString('N'))
    $taskStage=Join-Path $taskWork 'stage'
    $taskCore=Join-Path $taskWork 'core'
    New-Item -ItemType Directory -Force -Path $taskStage,$taskCore | Out-Null
    Write-Host "Fresh build workspace: $taskWork"
    $taskNode=Join-Path $DependencyRoot 'node.exe'
    & $taskNode ui/desktop/build.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    foreach ($taskProject in @('Desktop','NativeHost')) {
        $taskSubfolder=if ($taskProject -eq 'Desktop') {'app'} else {'host'}
        & $taskDotnet publish "src/Syphon.$taskProject/Syphon.$taskProject.csproj" -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:NuGetLockFilePath=packages.win-x64.lock.json "-p:Version=$Version" -p:DebugType=None -o (Join-Path $taskStage $taskSubfolder) --nologo
        if ($LASTEXITCODE -ne 0) { throw "$taskProject publish failed." }
    }
    & $taskDotnet publish src/Syphon.Setup/Syphon.Setup.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:NuGetLockFilePath=packages.win-x64.lock.json "-p:Version=$Version" -p:DebugType=None -o $taskCore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
    $taskRevision=(& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source revision.' }
    $taskDirty=[bool](& git status --porcelain | Select-Object -First 1)
    $taskBuildInputs=[ordered]@{
        version=$Version; architecture='win-x64'; sourceRevision=$taskRevision; sourceDirty=$taskDirty
        dotnetSdk=(& $taskDotnet --version).Trim(); nodeVersion=(& $taskNode --version).Trim()
        pythonVersion=(& python --version); powerShellVersion=$PSVersionTable.PSVersion.ToString()
        zlibVersion=(& python -c 'import zlib;print(zlib.ZLIB_VERSION)')
        frontendLockSha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot 'ui/charts/package-lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        dependencyLockSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'dependencies.win-x64.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $taskBuildInputs | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskStage 'build-inputs.json') -Encoding utf8
    $taskCoreExe=Join-Path $taskCore 'Syphon.Setup.exe'
    & python packaging/prepare-stage.py --version $Version --stage $taskStage --core $taskCoreExe --dependency-root $DependencyRoot
    if ($LASTEXITCODE -ne 0) { throw 'Dependency staging failed.' }
    & $taskDotnet src/Syphon.Setup/bin/Release/net10.0-windows10.0.19041.0/win-x64/Syphon.Setup.dll --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Installer engine checks failed.' }
    & python packaging/assemble-installer.py --output $OutputDirectory --version $Version --stage $taskStage --core $taskCoreExe
    if ($LASTEXITCODE -ne 0) { throw 'Installer assembly failed.' }
    Copy-Item -LiteralPath (Join-Path $taskStage 'build-inputs.json') -Destination (Join-Path $OutputDirectory 'build-inputs.json')
} finally { Pop-Location }
