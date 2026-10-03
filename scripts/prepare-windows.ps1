$ErrorActionPreference = 'Stop'
$version = '25.8.4.2'
$installer = Join-Path $env:RUNNER_TEMP 'LibreOffice.msi'
$extract = Join-Path $env:RUNNER_TEMP 'invoice-libreoffice'
$uri = "https://downloadarchive.documentfoundation.org/libreoffice/old/$version/win/x86_64/LibreOffice_${version}_Win_x86-64.msi"
Write-Host "Download official LibreOffice $version"
& curl.exe -4 --http1.1 --fail --location --retry 2 --retry-all-errors --connect-timeout 15 --max-time 180 --output $installer $uri
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Retry official download with Python/OpenSSL'
    python scripts/download-component.py $uri $installer
    if ($LASTEXITCODE -ne 0) { throw 'LibreOffice download failed' }
}
# Pinned upstream WinGet manifest provides the independent SHA-256 for this release.
$expected = 'B2B23D91BDA5AD6E97B38008B082060A55D2A3C7C269B2C0E78DACA865133D48'
if ((Get-FileHash $installer -Algorithm SHA256).Hash -ne $expected) { throw 'LibreOffice SHA-256 mismatch' }
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

# App-local VC runtime: clean Windows machines need no system-wide redist install.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio C++ redist files unavailable on builder' }
$crt = Get-ChildItem (Join-Path $visualStudio 'VC/Redist/MSVC') -Recurse -Directory -Filter Microsoft.VC143.CRT |
    Where-Object { $_.Parent.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $crt) { throw 'VC143 x64 CRT unavailable' }
New-Item -ItemType Directory -Force vendor/vc-runtime | Out-Null
Copy-Item (Join-Path $crt.FullName '*.dll') vendor/vc-runtime
Copy-Item (Join-Path $crt.FullName '*.dll') vendor/libreoffice/program
$redistNotice = Get-ChildItem (Join-Path $visualStudio 'VC') -Recurse -File -Filter 'REDIST.txt' | Select-Object -First 1
if ($redistNotice) { Copy-Item $redistNotice.FullName vendor/vc-runtime/REDIST.txt }

# Chinese fallback font for Word documents on English-only Windows systems.
$fontDirectory = 'vendor/libreoffice/share/fonts/truetype'
New-Item -ItemType Directory -Force $fontDirectory | Out-Null
$font = Join-Path $fontDirectory 'NotoSansCJKsc-Regular.otf'
$fontUri = 'https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf'
Invoke-WebRequest -Uri $fontUri -OutFile $font
if ((Get-FileHash $font -Algorithm SHA256).Hash -ne '2C76254F6FC379FDDFCE0A7E84FB5385BB135D3E399294F6EEB6680D0365B74B') {
    throw 'Chinese font SHA-256 mismatch'
}
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/Sans/LICENSE' -OutFile 'vendor/libreoffice/Noto-OFL.txt'
