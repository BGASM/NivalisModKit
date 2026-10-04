# Run a Nivalis ModKit dev command in the running game, from PowerShell or (through kit.cmd) Command Prompt.
#   kit                         list commands
#   kit demo style=Panel        run one (name=value arguments; quote values with spaces: kit notify "text=hi there")
# Needs the kit's [DevBridge] Enabled and AllowCommands. NIVALIS_GAME overrides the game folder.
param(
    [Parameter(Position = 0)][string]$Name,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$game = if ($env:NIVALIS_GAME) { $env:NIVALIS_GAME } else { 'H:\SteamLibrary\steamapps\common\Nivalis Nights' }
$base = 'http://127.0.0.1:5710'

function Show-Error($err) {
    $body = $err.ErrorDetails.Message
    if ($body) { try { Write-Host ($body | ConvertFrom-Json).error -ForegroundColor Yellow } catch { Write-Host $body -ForegroundColor Yellow } }
    else { Write-Host "No answer: is the game running with [DevBridge] Enabled?" -ForegroundColor Yellow }
}

try {
    if (-not $Name) {
        $list = Invoke-RestMethod "$base/cmd"
        if (-not $list.enabled) { Write-Host "Commands are off: set [DevBridge] AllowCommands = true in bgasm.nivalis.modkit.cfg" -ForegroundColor Yellow }
        $list.commands | Format-Table name, help -AutoSize -Wrap
        return
    }
    $tokenFile = Join-Path $game 'BepInEx\cache\nivalismodkit-bridge.token'
    if (-not (Test-Path $tokenFile)) { Write-Host "No token at $tokenFile (start the game with [DevBridge] Enabled)" -ForegroundColor Yellow; return }
    $token = (Get-Content $tokenFile -Raw).Trim()
    $query = ($Rest | Where-Object { $_ } | ForEach-Object {
        $k, $v = $_ -split '=', 2
        [uri]::EscapeDataString($k) + '=' + [uri]::EscapeDataString([string]$v)
    }) -join '&'
    $reply = Invoke-RestMethod -Method Post -Uri "$base/cmd/$Name`?$query" -Headers @{ 'X-Kit-Token' = $token }
    if ($reply -is [string]) { $reply } else { $reply | ConvertTo-Json -Depth 6 }
}
catch { Show-Error $_ }
