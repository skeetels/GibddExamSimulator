[CmdletBinding()]
param(
    [string] $Version = '2.0.0',
    [Parameter(Mandatory)]
    [string] $PwaWwwRoot,
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $workspaceRoot 'outputs'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$PwaWwwRoot = [IO.Path]::GetFullPath($PwaWwwRoot)

if (-not (Test-Path -LiteralPath $PwaWwwRoot -PathType Container)) {
    throw "Published PWA wwwroot was not found: $PwaWwwRoot"
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$sourceZip = Join-Path $OutputDirectory "GibddExamSimulator-Source-$Version.zip"
$pwaZip = Join-Path $OutputDirectory "GibddExamSimulator-PWA-$Version.zip"
foreach ($target in @($sourceZip, $pwaZip)) {
    if (Test-Path -LiteralPath $target) {
        throw "Release artifact already exists; remove or archive it explicitly before rebuilding: $target"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $PwaWwwRoot,
    $pwaZip,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$stream = [IO.File]::Open($sourceZip, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $stream,
        [IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $files = Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Force | Where-Object {
            $relative = [IO.Path]::GetRelativePath($projectRoot, $_.FullName).Replace('\', '/')
            $segments = $relative.Split('/')
            -not ($segments | Where-Object { $_ -in @('.git', '.vs', 'bin', 'obj', 'artifacts', 'publish') }) -and
            -not $relative.EndsWith('.user', [StringComparison]::OrdinalIgnoreCase) -and
            -not $relative.EndsWith('.suo', [StringComparison]::OrdinalIgnoreCase)
        }
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName).Replace('\', '/')
            $entryName = "GibddExamSimulator-$Version/$relative"
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $file.FullName,
                $entryName,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $OutputDirectory "README-$Version.md")
Copy-Item -LiteralPath (Join-Path $projectRoot 'NETWORK_SETUP.md') -Destination (Join-Path $OutputDirectory "NETWORK_SETUP-$Version.md")
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\branding\app-icon-1024.png') -Destination (Join-Path $OutputDirectory "GibddExamSimulator-Logo-$Version.png")

$artifacts = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object {
    $_.Name -in @(
        "GibddExamSimulator-Setup-$Version-win-x64.exe",
        "GibddExamSimulator-PWA-$Version.zip",
        "GibddExamSimulator-Source-$Version.zip",
        "GibddExamSimulator-Logo-$Version.png")
} | Sort-Object Name

$checksumPath = Join-Path $OutputDirectory "SHA256SUMS-$Version.txt"
$checksumLines = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash
    "$hash  $($artifact.Name)"
}
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

Write-Output "Source: $sourceZip"
Write-Output "PWA:    $pwaZip"
Write-Output "SHA256: $checksumPath"
