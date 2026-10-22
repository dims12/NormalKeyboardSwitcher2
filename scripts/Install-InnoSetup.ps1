[CmdletBinding()]
param([string]$Destination)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path $repoRoot 'artifacts\tools\InnoSetup' }
$Destination = [IO.Path]::GetFullPath($Destination)
$downloadDir = Join-Path $repoRoot 'artifacts\tools'
New-Item -ItemType Directory -Path $downloadDir -Force | Out-Null
$download = Join-Path $downloadDir 'innosetup-6.7.3.exe'
$expected = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
if (-not (Test-Path -LiteralPath $download)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' `
        -OutFile $download -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) {
    throw 'Inno Setup download checksum does not match the pinned compiler release.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Pyrsys B\.V\.') {
    throw 'Inno Setup download does not have the expected valid publisher signature.'
}
$process = Start-Process -FilePath $download -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', '/TASKS=""',
    "/DIR=`"$Destination`""
) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) { throw 'Inno Setup installation did not finish within 60 seconds.' }
if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
$compiler = Join-Path $Destination 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Inno Setup compiler is missing after installation.' }
Write-Output $compiler
