# Launches SAM.Game.exe in hidden background mode for every app ID listed in
# idle-list.txt, so Steam shows them as "in game" without any window or taskbar
# entry appearing. Runs once at login via the Startup folder entry that points
# to idle-games-silent.vbs.
#
# The list itself is managed from SAM.Picker (right-click a game -> "Idle at
# Startup"), which reads and rewrites idle-list.txt directly -- this script
# just replays whatever is currently in that file.

$listPath = Join-Path $PSScriptRoot 'idle-list.txt'
$exe = Join-Path $PSScriptRoot 'SAM.Game.exe'

if ((Test-Path $listPath) -eq $false) {
    exit
}

$appIds = Get-Content $listPath | ForEach-Object {
    $line = ($_ -split '#')[0].Trim()
    if ($line -match '^\d+$') { [uint32]$line }
} | Where-Object { $_ -ne $null }

foreach ($appId in $appIds) {
    Start-Process -FilePath $exe -ArgumentList "$appId --background" -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
    Start-Sleep -Seconds 2
}
