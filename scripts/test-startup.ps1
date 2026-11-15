param([string]$Executable = "$PSScriptRoot/../artifacts/win-x64/DiskLens.exe")
$ErrorActionPreference = 'Stop'
$LogPath = Join-Path $env:LOCALAPPDATA 'DiskLens/Logs/startup-latest.log'
if (Test-Path $LogPath) { Remove-Item $LogPath }
$process = Start-Process -FilePath (Resolve-Path $Executable).Path -ArgumentList '--startup-smoke-test' -PassThru
if (-not $process.WaitForExit(45000)) {
    $process.Kill()
    throw 'Published WPF startup timed out.'
}
if (Test-Path $LogPath) { Get-Content $LogPath }
if ($process.ExitCode -ne 0) { throw "Published WPF startup failed with exit code $($process.ExitCode)." }
if (-not (Test-Path $LogPath) -or -not (Select-String -Path $LogPath -Pattern 'STARTUP_SMOKE_TEST_PASSED' -Quiet)) {
    throw 'Published WPF application did not render its main window.'
}
