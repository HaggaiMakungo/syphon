param([switch]$Test, [switch]$Large, [switch]$Internet, [switch]$Media)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) {
    $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $taskRoot
try {
    # ChartAssets is bundled for ordinary .NET builds. Rebuild it after editing ui/charts.
    & $taskDotnet restore Syphon.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed. Install the SDK version from global.json or run bootstrap.ps1.' }
    & $taskDotnet build Syphon.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Test -or $Large -or $Internet -or $Media) {
        $taskArguments = @('tests\Syphon.Engine.Tests\bin\Release\net10.0\Syphon.Engine.Tests.dll', '--report', 'artifacts\test-results.json')
        if ($Large) { $taskArguments += '--large' }
        if ($Internet) { $taskArguments += '--internet' }
        & $taskDotnet @taskArguments
        if ($LASTEXITCODE -ne 0) { throw 'Engine integration tests failed.' }
        if ($Media) {
            & $taskDotnet tests\Syphon.Engine.Tests\bin\Release\net10.0\Syphon.Engine.Tests.dll --media-tests artifacts\media-tests.json
            if ($LASTEXITCODE -ne 0) { throw 'Media integration tests failed.' }
            & $taskDotnet tests\Syphon.Engine.Tests\bin\Release\net10.0\Syphon.Engine.Tests.dll --youtube-tests artifacts\youtube-tests.json
            if ($LASTEXITCODE -ne 0) { throw 'YouTube adapter tests failed.' }
        }
        & $taskDotnet tests\Syphon.Desktop.SmokeTests\bin\Release\net10.0-windows\Syphon.Desktop.SmokeTests.dll artifacts\desktop
        if ($LASTEXITCODE -ne 0) { throw 'Desktop smoke tests failed.' }
        & $taskDotnet tests\Syphon.Desktop.SmokeTests\bin\Release\net10.0-windows\Syphon.Desktop.SmokeTests.dll --floating-input artifacts\floating-input
        if ($LASTEXITCODE -ne 0) { throw 'Floating window input regression tests failed.' }
        & $taskDotnet tests\Syphon.Browser.Tests\bin\Release\net10.0-windows\Syphon.Browser.Tests.dll src\Syphon.NativeHost\bin\Release\net10.0-windows artifacts\browser-tests.json
        if ($LASTEXITCODE -ne 0) { throw 'Browser/native integration tests failed.' }
        $taskNode = (Get-Command node -ErrorAction Stop).Source
        & $taskNode --test tests\browser-extension.test.mjs tests\browser-features.test.mjs tests\browser-media.test.mjs tests\browser-context.test.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Browser extension tests failed.' }
    }
} finally { Pop-Location }
