<#
.SYNOPSIS
Publishes the self-contained win-x64 WPF application and compiles its Inno Setup installer.

.DESCRIPTION
This script is intentionally isolated from application source logic. It stages a clean
self-contained publish under artifacts/, then passes that folder to ISCC. The resulting
setup executable is written to the workspace outputs/ directory by default.

.EXAMPLE
.\Build-Installer.ps1 -AppVersion 2.0.2

.EXAMPLE
.\Build-Installer.ps1 -SkipPublish -PublishDirectory C:\staging\GibddExamSimulator -AppVersion 2.0.2
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string] $AppVersion = '2.0.2',

    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [string] $PublishDirectory,
    [string] $OutputDirectory,
    [string] $DotnetPath,
    [string] $InnoCompilerPath,
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$projectRoot = [IO.Path]::GetFullPath((Join-Path $installerDirectory '..'))
$workRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $workRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $artifactsRoot 'publish\win-x64'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $workspaceRoot 'outputs'
}
if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    $DotnetPath = Join-Path $workRoot '.dotnet-sdk-10\dotnet.exe'
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $InnoCompilerPath = Join-Path $workRoot 'tools\inno-6.7.3\ISCC.exe'
}

$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$DotnetPath = [IO.Path]::GetFullPath($DotnetPath)
$InnoCompilerPath = [IO.Path]::GetFullPath($InnoCompilerPath)

$appProject = Join-Path $projectRoot 'src\GibddExamSimulator.App\GibddExamSimulator.App.csproj'
$appExecutable = Join-Path $PublishDirectory 'GibddExamSimulator.exe'
$innoScript = Join-Path $installerDirectory 'GibddExamSimulator.iss'

if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
    throw "Application project was not found: $appProject"
}
if (-not (Test-Path -LiteralPath $innoScript -PathType Leaf)) {
    throw "Inno Setup script was not found: $innoScript"
}
if (-not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw "ISCC.exe was not found at '$InnoCompilerPath'. Run Prepare-InnoSetup.ps1 first."
}

if (-not $SkipPublish) {
    if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
        throw ".NET SDK was not found at '$DotnetPath'."
    }

    $allowedPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $PublishDirectory.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "For a safe clean publish, PublishDirectory must be below '$artifactsRoot'. Use -SkipPublish to package an external staging directory."
    }

    if (Test-Path -LiteralPath $PublishDirectory) {
        Remove-Item -LiteralPath $PublishDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null

    $env:DOTNET_CLI_HOME = Join-Path $workRoot '.dotnet-home-10'
    $env:NUGET_PACKAGES = Join-Path $workRoot '.nuget\packages10'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME -Force | Out-Null
    New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null

    $publishArguments = @(
        'publish',
        $appProject,
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $PublishDirectory,
        '-p:PublishSingleFile=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:ContinuousIntegrationBuild=true',
        "-p:Version=$AppVersion"
    )

    & $DotnetPath @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Published application executable was not found: $appExecutable"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$compilerArguments = @(
    '/Qp',
    "/DAppVersion=$AppVersion",
    "/DPublishDir=$PublishDirectory",
    "/DOutputDir=$OutputDirectory",
    $innoScript
)

& $InnoCompilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $OutputDirectory "GibddExamSimulator-Setup-$AppVersion-win-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "ISCC reported success, but the installer was not found: $installerPath"
}

$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
Write-Output "Installer: $installerPath"
Write-Output "SHA256:   $installerHash"
