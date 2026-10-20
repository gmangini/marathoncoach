<#
.SYNOPSIS
  Deploys the committed marathoncoach code to your Docker server over SSH.

.DESCRIPTION
  Dry run by default: prints what it would do. Add -Apply to do it.
  1. Packs the committed code (git archive HEAD), so uncommitted and git-ignored files never ship.
  2. Copies it to ~/marathoncoach on the server and runs: docker compose up -d --build
  With -CopySecrets (first deploy, or after changing them) it also:
  - copies your local .env to the server (mode 600), adding TZ if it is missing
  - pipes your Garmin token file into the data volume as the bot's own user (no 2FA needed on the server)
  Needs ssh/scp (Windows OpenSSH) and an SSH key the server accepts.

.EXAMPLE
  pwsh scripts/Deploy-Server.ps1 -Server root@203.0.113.10 -CopySecrets          # dry run
  pwsh scripts/Deploy-Server.ps1 -Server root@203.0.113.10 -CopySecrets -Apply   # first deploy
  pwsh scripts/Deploy-Server.ps1 -Server root@203.0.113.10 -Apply                # later code updates
#>
param(
    [Parameter(Mandatory)][string]$Server,
    [switch]$CopySecrets,
    [string]$TimeZone = 'America/Denver',
    [string]$TokenPath = (Join-Path $HOME '.marathoncoach-garmin-token.json'),
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$remoteDir = 'marathoncoach'

function Step([string]$text) { Write-Output "$(if ($Apply) { '>>' } else { 'would' }) $text" }
function Run([string]$exe, [string[]]$argv) {
    if (-not $Apply) { return }
    & $exe @argv
    if ($LASTEXITCODE -ne 0) { throw "$exe failed (exit $LASTEXITCODE)" }
}

Push-Location $repo
try {
    $dirty = git status --porcelain
    if ($dirty) { Write-Warning "Uncommitted changes are NOT deployed (only HEAD ships):`n$($dirty -join "`n")" }
    $head = git rev-parse --short HEAD
    $archive = Join-Path ([IO.Path]::GetTempPath()) "marathoncoach-$head.tgz"

    Step "pack commit $head -> $archive"
    Run git @('archive', '--format=tar.gz', '-o', $archive, 'HEAD')

    Step "copy it to ${Server}:~/$remoteDir and unpack"
    Run scp @('-q', $archive, "${Server}:/tmp/marathoncoach.tgz")
    Run ssh @($Server, "mkdir -p ~/$remoteDir && tar xzf /tmp/marathoncoach.tgz -C ~/$remoteDir && rm /tmp/marathoncoach.tgz")

    if ($CopySecrets) {
        $envFile = Join-Path $repo '.env'
        if (-not (Test-Path $envFile)) { throw "No .env at $envFile" }
        $lines = Get-Content $envFile
        if (-not ($lines -match '^TZ=.+')) { $lines += "TZ=$TimeZone" }
        $tmpEnv = Join-Path ([IO.Path]::GetTempPath()) "marathoncoach-$([guid]::NewGuid().ToString('N')).env"
        Step "copy .env to the server (mode 600, TZ=$((($lines -match '^TZ=') -replace '^TZ=','') | Select-Object -First 1))"
        if ($Apply) {
            try {
                Set-Content -Path $tmpEnv -Value $lines -Encoding utf8NoBOM
                Run scp @('-q', $tmpEnv, "${Server}:~/$remoteDir/.env")
                Run ssh @($Server, "chmod 600 ~/$remoteDir/.env")
            }
            finally { Remove-Item $tmpEnv -ErrorAction SilentlyContinue }
        }

        if (Test-Path $TokenPath) {
            Step "put the Garmin token into the data volume (written by the bot's own user)"
            if ($Apply) {
                # Piped over ssh into a one-off container, so it lands in the volume owned by 'app' and is never
                # left as a loose file on the server.
                Get-Content -Raw -LiteralPath $TokenPath |
                    ssh $Server "cd ~/$remoteDir && docker compose build -q && docker compose run --rm -T --entrypoint sh bot -c 'cat > /app/data/garmin-token.json'"
                if ($LASTEXITCODE -ne 0) { throw "copying the Garmin token failed (exit $LASTEXITCODE)" }
            }
        }
        else { Write-Warning "No Garmin token at ${TokenPath}: run the bot once on this PC first (it logs in and saves it)." }
    }

    Step "start or update the bot: docker compose up -d --build"
    Run ssh @($Server, "cd ~/$remoteDir && docker compose up -d --build && sleep 20 && docker compose logs --tail 15 bot")

    if (-not $Apply) { Write-Output "DRY RUN: nothing changed. Add -Apply to deploy." ; exit 5 }
    Write-Output "Deployed $head to $Server. Stop the copy on your PC so DMs aren't answered twice."
}
finally {
    if ($archive -and (Test-Path $archive)) { Remove-Item $archive -ErrorAction SilentlyContinue }
    Pop-Location
}
