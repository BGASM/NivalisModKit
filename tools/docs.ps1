# Build the documentation site into docs/ (served by GitHub Pages from main /docs).
#   tools\docs.ps1           build
#   tools\docs.ps1 -Serve    build, then preview at http://localhost:8080
# Runs locally: the API pages need the kit to compile, which needs the game's assemblies (Directory.Build.props).
param([switch]$Serve)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docfx = Join-Path $root 'docfx'

# The guide is the README. Links that only work inside the repo become GitHub links.
$repo = 'https://github.com/BGASM/NivalisModKit/tree/main'
$readme = Get-Content (Join-Path $root 'README.md') -Raw -Encoding UTF8
$readme = $readme -replace '\]\((samples/[^)]*)\)', "]($repo/`$1)"
$readme = $readme -replace '\]\((tools/[^)]*)\)', "]($repo/`$1)"
$readme = $readme -replace '\]\(NivalisModKit\.slnx\)', "]($repo/NivalisModKit.slnx)"
Set-Content (Join-Path $docfx 'index.md') $readme -Encoding UTF8 -NoNewline

Push-Location $docfx
try {
    docfx metadata docfx.json
    if ($LASTEXITCODE -ne 0) { throw "docfx metadata failed" }
    docfx build docfx.json
    if ($LASTEXITCODE -ne 0) { throw "docfx build failed" }
}
finally { Pop-Location }

# The theme ships source maps for debugging its own scripts; the site doesn't need them (about half its size).
Get-ChildItem (Join-Path $root 'docs') -Recurse -Filter *.map | Remove-Item

# GitHub Pages runs Jekyll by default, which hides folders starting with "_"; turn it off.
New-Item -ItemType File -Force (Join-Path $root 'docs\.nojekyll') | Out-Null
Write-Host "Site built in docs\. Commit docs\ to publish." -ForegroundColor Green

if ($Serve) { docfx serve (Join-Path $root 'docs') }
