# Starts up to 30 eligible SAM background processes from idle-list.txt.
# Fallout: New Vegas and all Skyrim editions are explicitly excluded.

$listPath = Join-Path $PSScriptRoot 'idle-list.txt'
$exe = Join-Path $PSScriptRoot 'SAM.Game.exe'
$maxIdleGames = 30
$excludedIds = @(22380, 72850, 489830, 611670)

if (!(Test-Path $listPath) -or !(Test-Path $exe)) {
    exit
}

$seen = [System.Collections.Generic.HashSet[uint32]]::new()
$appIds = foreach ($rawLine in Get-Content $listPath) {
    $parts = $rawLine -split '#', 2
    $idText = $parts[0].Trim()
    $name = if ($parts.Count -gt 1) { $parts[1].Trim() } else { '' }

    [uint32]$id = 0
    if (![uint32]::TryParse($idText, [ref]$id)) {
        continue
    }

    $lowerName = $name.ToLowerInvariant()
    if ($excludedIds -contains $id -or
        $lowerName.Contains('skyrim') -or
        $lowerName.Contains('fallout: new vegas') -or
        $lowerName.Contains('fallout new vegas')) {
        continue
    }

    if ($seen.Add($id)) {
        $id
    }
}

$appIds = @($appIds | Select-Object -First $maxIdleGames)

foreach ($appId in $appIds) {
    Start-Process -FilePath $exe `
        -ArgumentList "$appId --background" `
        -WorkingDirectory $PSScriptRoot `
        -WindowStyle Hidden

    Start-Sleep -Seconds 2
}