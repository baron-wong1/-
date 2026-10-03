$ErrorActionPreference = 'Stop'
$version = '25.8.4.2'
$installer = Join-Path $env:RUNNER_TEMP 'LibreOffice.msi'
$extract = Join-Path $env:RUNNER_TEMP 'invoice-libreoffice'
$uri = "https://downloadarchive.documentfoundation.org/libreoffice/old/$version/win/x86_64/LibreOffice_${version}_Win_x86-64.msi"
Write-Host "Download official LibreOffice $version"
Invoke-WebRequest -Uri $uri -OutFile $installer
$signature = Get-AuthenticodeSignature $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'The Document Foundation') {
    throw "LibreOffice installer signature validation failed: $($signature.Status)"
}
$process = Start-Process msiexec.exe -Wait -PassThru -ArgumentList @('/a', "`"$installer`"", '/qn', "TARGETDIR=`"$extract`"")
if ($process.ExitCode -notin @(0, 3010)) { throw "LibreOffice extraction failed: $($process.ExitCode)" }
$exe = Get-ChildItem $extract -Recurse -Filter soffice.exe | Select-Object -First 1
if (-not $exe) { throw 'Extracted LibreOffice has no soffice.exe' }
New-Item -ItemType Directory -Force vendor | Out-Null
if (Test-Path vendor/libreoffice) { Remove-Item vendor/libreoffice -Recurse -Force }
Copy-Item $exe.Directory.Parent.FullName vendor/libreoffice -Recurse
Write-Host 'LibreOffice prepared with share files, fonts, and licenses.'
