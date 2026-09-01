<#
.SYNOPSIS
Downloads, verifies, and installs the pinned Inno Setup compiler in portable mode under work/.
#>
[CmdletBinding()]
param(
    [string] $DestinationDirectory,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$innoVersion = '6.7.3'
$officialUri = [Uri]'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$expectedSha256 = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'

$installerDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$projectRoot = [IO.Path]::GetFullPath((Join-Path $installerDirectory '..'))
$workRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $workRoot "tools\inno-$innoVersion"
}

$DestinationDirectory = [IO.Path]::GetFullPath($DestinationDirectory)
$downloadDirectory = Join-Path $workRoot 'tools\downloads'
$downloadPath = Join-Path $downloadDirectory "innosetup-$innoVersion.exe"
$isccPath = Join-Path $DestinationDirectory 'ISCC.exe'

function Assert-OfficialInstaller {
    param([Parameter(Mandatory)][string] $Path)

    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedSha256) {
        throw "Inno Setup SHA256 mismatch. Expected $expectedSha256, received $actualHash."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Inno Setup Authenticode signature is not valid: $($signature.StatusMessage)"
    }
    if ($signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.') {
        throw "Unexpected Inno Setup signer: $($signature.SignerCertificate.Subject)"
    }
}

if ((Test-Path -LiteralPath $isccPath -PathType Leaf) -and -not $Force) {
    $signature = Get-AuthenticodeSignature -LiteralPath $isccPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Existing ISCC.exe signature is not valid: $($signature.StatusMessage)"
    }
    Write-Output "Inno Setup $innoVersion is already available: $isccPath"
    return
}

New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

if ($Force -or -not (Test-Path -LiteralPath $downloadPath -PathType Leaf)) {
    Invoke-WebRequest -UseBasicParsing -Uri $officialUri -OutFile $downloadPath
}

Assert-OfficialInstaller -Path $downloadPath

$setupArguments = @(
    '/PORTABLE=1',
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/SP-',
    "/DIR=`"$DestinationDirectory`""
)

$process = Start-Process -FilePath $downloadPath -ArgumentList $setupArguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Inno Setup portable installation failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $isccPath -PathType Leaf)) {
    throw "Portable installation completed, but ISCC.exe was not found: $isccPath"
}

$compilerSignature = Get-AuthenticodeSignature -LiteralPath $isccPath
if ($compilerSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Installed ISCC.exe signature is not valid: $($compilerSignature.StatusMessage)"
}

Write-Output "Inno Setup $innoVersion prepared successfully: $isccPath"
Write-Output "Source: $officialUri"
Write-Output "SHA256: $expectedSha256"
