$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\hooks.json'
if (Test-Path -LiteralPath $destination) { throw 'hooks.json already exists. Review and merge existing hooks before installation.' }
if (-not (Test-Path -LiteralPath (Join-Path $root 'GoldenMonkeyPet-v13.exe'))) { throw 'Build v13 first.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'codex-hooks.json') -Destination $destination
Write-Output ('Installed hooks: ' + $destination)
Write-Output 'Codex requires review and trust of these hooks before they can run. No trust bypass was configured.'
