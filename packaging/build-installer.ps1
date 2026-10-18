[CmdletBinding()]
param([string]$OutputDirectory, [string]$Version='0.1.0-beta.15', [string]$DependencyRoot)
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
    if ($LASTEXITCODE -ne 0) { throw 'Import the pinned release dependencies before building. See packaging/README.md.' }
    & python tests/media_runtime_test.py
    if ($LASTEXITCODE -ne 0) { throw 'Media runtime regression checks failed.' }
    & python tests/yt_dlp_build_test.py
    if ($LASTEXITCODE -ne 0) { throw 'Media build regression checks failed.' }
    & python packaging/media_runtime.py (Join-Path $DependencyRoot 'yt-dlp.exe')
    if ($LASTEXITCODE -ne 0) { throw 'The pinned yt-dlp runtime did not pass release checks.' }
    $taskBootstrap=Join-Path $DependencyRoot 'MicrosoftEdgeWebview2Setup.exe'
    $taskSignature=Get-AuthenticodeSignature -LiteralPath $taskBootstrap
    if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'WebView2 bootstrapper must have a valid Microsoft signature.' }
    if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'ui/charts/node_modules/esbuild/lib/main.js'))) { throw 'Prepare the frontend dependencies first: npm ci --prefix ui/charts' }
    & python tests/packaging_test.py
    if ($LASTEXITCODE -ne 0) { throw 'Packaging regression checks failed.' }
    $taskWork=Join-Path $taskRoot ('artifacts\installer-work\'+[Guid]::NewGuid().ToString('N'))
    $taskStage=Join-Path $taskWork 'stage'
    $taskCore=Join-Path $taskWork 'core'
    $taskChecker=Join-Path $taskWork 'setup-checker'
    $taskCheckerObj=Join-Path $taskWork 'setup-checker-obj'
    $taskProductionBuild=Join-Path $taskWork 'setup-production'
    $taskProductionObj=Join-Path $taskWork 'setup-production-obj'
    New-Item -ItemType Directory -Force -Path $taskStage,$taskCore | Out-Null
    Write-Host "Fresh build workspace: $taskWork"
    $taskNode=Join-Path $DependencyRoot 'node.exe'
    & $taskNode ui/desktop/build.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    foreach ($taskProject in @('Desktop','NativeHost')) {
        $taskSubfolder=if ($taskProject -eq 'Desktop') {'app'} else {'host'}
        & $taskDotnet publish "src/Syphon.$taskProject/Syphon.$taskProject.csproj" -p:SyphonPackagedBuild=true -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:NuGetLockFilePath=packages.win-x64.lock.json "-p:Version=$Version" -p:DebugType=None -o (Join-Path $taskStage $taskSubfolder) --nologo
        if ($LASTEXITCODE -ne 0) { throw "$taskProject publish failed." }
    }
    # Diagnostic commands exist only in this isolated development checker.
    & $taskDotnet build src/Syphon.Setup/Syphon.Setup.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:NuGetLockFilePath=packages.win-x64.lock.json -p:EnableSetupDiagnostics=true -p:PublishSingleFile=true "-p:OutputPath=$taskChecker/" "-p:IntermediateOutputPath=$taskCheckerObj/" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Setup diagnostic checker build failed.' }
    & $taskDotnet (Join-Path $taskChecker 'Syphon.Setup.dll') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Installer engine checks failed.' }
    # The delivered core is compiled separately with diagnostics explicitly disabled.
    & $taskDotnet publish src/Syphon.Setup/Syphon.Setup.csproj -p:EnableSetupDiagnostics=false "-p:OutputPath=$taskProductionBuild/" "-p:IntermediateOutputPath=$taskProductionObj/" -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:NuGetLockFilePath=packages.win-x64.lock.json "-p:Version=$Version" -p:DebugType=None -o $taskCore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
    & $taskDotnet (Join-Path $taskChecker 'Syphon.Setup.dll') --verify-production (Join-Path $taskProductionBuild 'Syphon.Setup.dll') $taskDotnet (Join-Path $taskWork 'setup-production-checks.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Production installer diagnostic isolation checks failed.' }
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
    & python packaging/assemble-installer.py --output $OutputDirectory --version $Version --stage $taskStage --core $taskCoreExe
    if ($LASTEXITCODE -ne 0) { throw 'Installer assembly failed.' }
    & $taskDotnet (Join-Path $taskChecker 'Syphon.Setup.dll') --verify-package (Join-Path $taskWork 'package-check') (Join-Path $OutputDirectory "Syphon-Setup-$Version-win-x64.exe")
    if ($LASTEXITCODE -ne 0) { throw 'Assembled package verification failed.' }
    Copy-Item -LiteralPath (Join-Path $taskStage 'build-inputs.json') -Destination (Join-Path $OutputDirectory 'build-inputs.json')
} finally { Pop-Location }
