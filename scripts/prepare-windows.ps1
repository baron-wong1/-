$ErrorActionPreference = 'Stop'
$version = '26.8.0'
$installer = Join-Path $env:RUNNER_TEMP 'LibreOffice.msi'
$extract = Join-Path $env:RUNNER_TEMP 'invoice-libreoffice'
$uri = "https://download.documentfoundation.org/libreoffice/stable/$version/win/x86_64/LibreOffice_${version}_Win_x86-64.msi"
Write-Host "Download official LibreOffice $version"
$relative = "libreoffice/stable/$version/win/x86_64/LibreOffice_${version}_Win_x86-64.msi"
# Established TDF mirrors avoid a slow automatic redirect; every result still needs
# the independent upstream hash and the publisher's Authenticode signature below.
$mirrors = @("https://ftp.osuosl.org/pub/tdf/$relative", "https://mirror.init7.net/tdf/$relative", $uri)
$downloaded = $false
foreach ($mirror in $mirrors) {
    Write-Host "Try runtime mirror: $mirror"
    curl.exe --fail --location --connect-timeout 15 --max-time 600 --speed-time 20 --speed-limit 100000 --output $installer --write-out "`nEffective URL: %{url_effective}`n" $mirror
    if ($LASTEXITCODE -eq 0) { $downloaded = $true; break }
}
if (-not $downloaded) { throw 'Official LibreOffice mirrors unavailable or too slow; retry the build later' }
# Pinned upstream WinGet manifest provides the independent SHA-256 for this release.
$expected = '4AA6C6E1895F4055104EFFCB556BD3362D20C6AD707C149543304F395EF9DB95'
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
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/LICENSE' -OutFile 'vendor/libreoffice/Noto-OFL.txt'
