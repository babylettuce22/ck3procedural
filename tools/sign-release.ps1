# Signs every .exe/.dll under the given folders/files with the Certum cloud cert (SimplySign).
# Run BEFORE building the release archive. SimplySign Desktop must be logged in (token from the phone app).
#   powershell -File tools\sign-release.ps1 path\to\publish[,another\file.exe]
param(
    [Parameter(Mandatory, Position = 0)][string[]]$Path,
    [string]$Thumbprint = 'BA24D3E74FC554E003D28DE18F1E1418B1E68C9F',
    [string]$Description = 'CK3 Procedural Generator'
)
$ErrorActionPreference = 'Stop'

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe |
    Where-Object FullName -like '*\x64\*' | Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
if (-not $signtool) { throw 'signtool.exe not found (install the Windows SDK signing tools).' }

if (-not (Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $Thumbprint)) {
    throw 'Certificate not in the store. Log in to SimplySign Desktop (fresh token from the phone app) and retry.'
}

$files = foreach ($p in $Path) {
    if (Test-Path $p -PathType Container) { Get-ChildItem $p -Recurse -File -Include *.exe, *.dll } else { Get-Item $p }
}
# Skip anything already signed (third-party DLLs keep their own signature).
$todo = $files | Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -eq 'NotSigned' }
if (-not $todo) { Write-Host 'Nothing to sign.'; return }

foreach ($f in $todo) {
    Write-Host "Signing $($f.FullName)"
    & $signtool sign /sha1 $Thumbprint /fd SHA256 /tr http://time.certum.pl /td SHA256 /d $Description $f.FullName
    if ($LASTEXITCODE -ne 0) { throw "signtool failed on $($f.Name)" }
}
Write-Host "Signed $(@($todo).Count) file(s)."
