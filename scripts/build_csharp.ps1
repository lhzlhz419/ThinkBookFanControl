param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "csharp\ThinkBookFanControl\ThinkBookFanControl.csproj"
$currentBranchOutput = git -C $root branch --show-current
if ($LASTEXITCODE -ne 0) {
    throw "Unable to determine the current Git branch."
}
$currentBranch = ([string]($currentBranchOutput | Select-Object -First 1)).Trim()
$distributionDir = Join-Path $root $(if ($currentBranch -eq "dev") { "dist-dev" } else { "dist" })
$selfContainedPublishDir = Join-Path $distributionDir "ThinkBookFanControl-win-x64"
$frameworkDependentPublishDir = Join-Path $distributionDir "ThinkBookFanControl-win-x64-net9-runtime"
$selfContainedZip = "$selfContainedPublishDir.zip"
$frameworkDependentZip = "$frameworkDependentPublishDir.zip"
$publishBuildDir = Join-Path $root ".tmp\csharp-publish-bin"
$legacyOutputDir = Join-Path $root "csharp\ThinkBookFanControl\bin\$Configuration\net9.0-windows\win-x64"

function Assert-SafePath {
    param([Parameter(Mandatory)][string]$Path)

    $fullRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the workspace: $fullPath"
    }
    return $fullPath
}

function Remove-SafeDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = Assert-SafePath $Path
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Remove-SafeFile {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = Assert-SafePath $Path
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Force
    }
}

$info = dotnet --info 2>&1 | Out-String
if ($info -match "No SDKs were found") {
    throw "No .NET SDK is installed. Install the .NET 9 SDK, then re-run this script."
}

$running = Get-Process -Name "ThinkBookFanControl" -ErrorAction SilentlyContinue |
    Where-Object { (-not $_.Path) -or ($_.Path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) }
if ($running) {
    throw "ThinkBookFanControl.exe is running. Close all ThinkBookFanControl windows before building."
}

if ($Publish) {
    Remove-SafeDirectory $selfContainedPublishDir
    Remove-SafeDirectory $frameworkDependentPublishDir
    Remove-SafeDirectory $publishBuildDir
    Remove-SafeDirectory $legacyOutputDir
    Remove-SafeFile $selfContainedZip
    Remove-SafeFile $frameworkDependentZip
    dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $selfContainedPublishDir /p:PublishSingleFile=false "/p:BaseOutputPath=$publishBuildDir/"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    dotnet publish $project -c $Configuration -r win-x64 --self-contained false -o $frameworkDependentPublishDir /p:PublishSingleFile=false "/p:BaseOutputPath=$publishBuildDir/"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    Compress-Archive -LiteralPath $selfContainedPublishDir -DestinationPath $selfContainedZip -CompressionLevel Optimal
    Compress-Archive -LiteralPath $frameworkDependentPublishDir -DestinationPath $frameworkDependentZip -CompressionLevel Optimal
    Write-Host "Publish output (self-contained): $selfContainedPublishDir"
    Write-Host "Publish output (.NET 9 runtime required): $frameworkDependentPublishDir"
    Write-Host "ZIP output (self-contained): $selfContainedZip"
    Write-Host "ZIP output (.NET 9 runtime required): $frameworkDependentZip"
} else {
    dotnet build $project -c $Configuration
}

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
