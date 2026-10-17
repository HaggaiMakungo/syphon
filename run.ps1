$ErrorActionPreference = 'Stop'
$taskDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $PSScriptRoot
try {
    & $taskDotnet run --project src\Syphon.Desktop -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Syphon could not start. Run build.ps1 first.' }
} finally { Pop-Location }
