# Signs a GitHub release's CI zip and sends it on to Nexus. The only manual step is logging in to
# SimplySign Desktop (token from the phone app) before running it.
#
#   powershell -ExecutionPolicy Bypass -File tools\release-signed.ps1 -Tag v0.96
#   ... -Tag v0.96 -NotesFile notes.md -Title "Procedural Tool v0.96" -Prerelease   (creates the release first)
#
# Flow: [create release + tag] -> wait for release.yml to attach Ck3MapGen-*-win-x64.zip -> download it ->
# sign our own binaries inside it (third-party DLLs stay as their authors shipped them) -> replace the
# release asset -> dispatch nexus.yml (which is skipped on the release event while ATTACH_CI_BUILD is on).
# The build itself stays in CI, so the signed zip holds exactly the binaries CI built.
#   ... -LocalZip path\to\Ck3MapGen-x-win-x64.zip   (just sign that zip in place; no GitHub, no Nexus)
param(
    [string]$Tag,
    [string]$LocalZip,
    [string]$NotesFile,
    [string]$Title,
    [switch]$Prerelease,
    [switch]$NoNexus,
    # Our binaries, matched against paths inside the zip.
    [string[]]$Sign = @('CK3 Procedural Generator.exe', 'app/Ck3MapGen.exe', 'app/Ck3MapGen.dll', 'app/NoiseTool.*.dll')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$signer = Join-Path $PSScriptRoot 'sign-release.ps1'
$thumb = 'BA24D3E74FC554E003D28DE18F1E1418B1E68C9F'

# Not named "gh": PowerShell names are case-insensitive, so the function would call itself.
# gh writes progress to stderr, which PowerShell 5.1 turns into a terminating error under Stop; judge by exit code.
function Invoke-Gh {
    $ErrorActionPreference = 'Continue'
    & gh.exe @args
    if ($LASTEXITCODE -ne 0) { throw "gh $($args -join ' ') failed" }
}

# Fail before anything is published if signing can't work.
if (-not (Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $thumb)) {
    throw 'Code-signing cert not in the store. Log in to SimplySign Desktop (fresh token from the phone app) and retry.'
}

if ($LocalZip) { $zipPath = (Resolve-Path $LocalZip).Path; $work = Split-Path $zipPath }
elseif (-not $Tag) { throw 'Pass -Tag (or -LocalZip).' }
else {

# 1. Create the release (fires release.yml through the new tag) unless it already exists.
$ErrorActionPreference = 'Continue'
& gh.exe release view $Tag --json tagName 2>$null | Out-Null
$exists = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if (-not $exists) {
    if (-not $NotesFile) { throw "Release $Tag doesn't exist. Pass -NotesFile (and -Title) to create it." }
    $create = @('release', 'create', $Tag, '--target', 'main', '--notes-file', $NotesFile)
    if ($Title) { $create += @('--title', $Title) }
    if ($Prerelease) { $create += '--prerelease' }
    Invoke-Gh @create
    Write-Host "Created release $Tag."
}

# 2. Wait for release.yml's run on that tag to finish (it attaches the zip).
$run = $null
for ($i = 0; $i -lt 30 -and -not $run; $i++) {
    $run = (Invoke-Gh run list --workflow release.yml --branch $Tag -L 1 --json databaseId -q '.[0].databaseId')
    if (-not $run) { Start-Sleep 10 }
}
if (-not $run) { throw "No release.yml run found for $Tag." }
Write-Host "Waiting for release.yml run $run ..."
Invoke-Gh run watch $run --exit-status --interval 30 | Out-Null

# 3. Download the attached zip.
$work = Join-Path $env:TEMP "release-signed-$Tag"
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null
Invoke-Gh release download $Tag -p 'Ck3MapGen-*-win-x64.zip' -D $work
$zipPath = (Get-ChildItem $work -Filter *.zip | Select-Object -First 1).FullName
}

# 4. Pull out our binaries, sign them, write them back into the zip (everything else untouched).
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Update')
try {
    $entries = @($zip.Entries | Where-Object { $n = $_.FullName -replace '\\', '/'; $Sign | Where-Object { $n -like $_ } })
    if ($entries.Count -lt $Sign.Count) { throw "Expected at least $($Sign.Count) binaries to sign, found: $($entries.FullName -join ', ')" }
    $stage = Join-Path $work 'stage'
    $staged = foreach ($e in $entries) {
        $dst = Join-Path $stage $e.FullName
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dst, $true)
        [pscustomobject]@{ Entry = $e; File = $dst }
    }
    & $signer -Path $staged.File -Thumbprint $thumb
    foreach ($s in $staged) {
        if ((Get-AuthenticodeSignature $s.File).Status -ne 'Valid') { throw "$($s.File) did not end up with a valid signature." }
        $name = $s.Entry.FullName
        $s.Entry.Delete()
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $s.File, $name, 'Optimal')
    }
} finally { $zip.Dispose() }
Write-Host "Signed $($staged.Count) binaries inside $(Split-Path $zipPath -Leaf)."
if ($LocalZip) { return }

# 5. Replace the unsigned asset with the signed one (same name).
Invoke-Gh release upload $Tag $zipPath --clobber
Write-Host "Uploaded signed zip to $Tag."

# 6. Send it to Nexus (nexus.yml re-checks it with VirusTotal first).
if (-not $NoNexus) {
    Invoke-Gh workflow run nexus.yml -f tag=$Tag
    Write-Host 'Dispatched nexus.yml. Follow it with: gh run list --workflow nexus.yml -L 1'
}
