#requires -Version 5.1
[CmdletBinding()]
param([switch]$Uninstall, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskHostDirectory = Join-Path $taskRoot '.tools\browser-host'
$taskManifestPath = Join-Path $taskHostDirectory 'com.syphon.downloads.json'
$taskRegistryPaths = @('Software\Google\Chrome\NativeMessagingHosts\com.syphon.downloads', 'Software\Microsoft\Edge\NativeMessagingHosts\com.syphon.downloads')
if ($Uninstall) {
    foreach ($taskRegistryPath in $taskRegistryPaths) {
        $taskKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskRegistryPath)
        if ($taskKey) {
            $taskOwned = $taskKey.GetValue('') -eq $taskManifestPath
            $taskKey.Dispose()
            if ($taskOwned) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($taskRegistryPath) }
        }
    }
    Write-Output 'Syphon native host disconnected. Remove the extension in Chrome/Edge if desired. Downloads and task history were preserved.'
    return
}
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $NoBuild) {
    & $taskDotnet build (Join-Path $taskRoot 'Syphon.sln') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build Syphon successfully before connecting the browser.' }
}
$taskHostBuild = Join-Path $taskRoot 'src\Syphon.NativeHost\bin\Release\net10.0-windows'
$taskDesktop = Join-Path $taskRoot 'src\Syphon.Desktop\bin\Release\net10.0-windows\Syphon.exe'
if (-not (Test-Path -LiteralPath (Join-Path $taskHostBuild 'Syphon.NativeHost.exe')) -or -not (Test-Path -LiteralPath $taskDesktop)) { throw 'Run build.ps1 first.' }
$taskExtension = Get-Content -LiteralPath (Join-Path $taskRoot 'extensions\syphon\manifest.json') -Raw | ConvertFrom-Json
$taskHasher = [System.Security.Cryptography.SHA256]::Create()
try { $taskHash = $taskHasher.ComputeHash([Convert]::FromBase64String($taskExtension.key)) }
finally { $taskHasher.Dispose() }
$taskId = -join ($taskHash[0..15] | ForEach-Object { [char](97 + ($_ -shr 4)); [char](97 + ($_ -band 15)) })
New-Item -ItemType Directory -Path $taskHostDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $taskHostBuild -File | Where-Object { $_.Extension -in '.exe', '.dll', '.json' } | Copy-Item -Destination $taskHostDirectory -Force
$taskOrigin = "chrome-extension://$taskId/"
$taskUtf8 = New-Object System.Text.UTF8Encoding($false)
$taskManifest = @{ name = 'com.syphon.downloads'; description = 'Syphon this download link'; path = (Join-Path $taskHostDirectory 'Syphon.NativeHost.exe'); type = 'stdio'; allowed_origins = @($taskOrigin) } | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($taskManifestPath, $taskManifest, $taskUtf8)
$taskSettings = @{ desktopPath = $taskDesktop; allowedOrigins = @($taskOrigin) }
if (Test-Path -LiteralPath (Join-Path $taskRoot '.tools\dotnet\dotnet.exe')) { $taskSettings.dotnetRoot = Join-Path $taskRoot '.tools\dotnet' }
[System.IO.File]::WriteAllText((Join-Path $taskHostDirectory 'host-settings.json'), ($taskSettings | ConvertTo-Json -Depth 4), $taskUtf8)
foreach ($taskRegistryPath in $taskRegistryPaths) {
    $taskKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($taskRegistryPath)
    try { $taskKey.SetValue('', $taskManifestPath, [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $taskKey.Dispose() }
}
Write-Output "Native host connected for Chrome and Edge. Extension ID: $taskId"
Write-Output "Load unpacked extension folder: $(Join-Path $taskRoot 'extensions\syphon')"
Write-Output 'Then right-click an HTTP/HTTPS link and choose Syphon this. No administrator access is required.'
