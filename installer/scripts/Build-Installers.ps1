[CmdletBinding()]
param(
    [string]$Version = "0.0.3-alpha",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$BasePayloadDirectory = "",
    [string]$BaseVersion = "0.0.2-alpha",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$installerRoot = Join-Path $repoRoot "installer"
$workRoot = Join-Path $installerRoot ".work\$Version-$Runtime"
$artifactRoot = Join-Path $installerRoot "artifacts\$Version-$Runtime"
$appPublish = Join-Path $workRoot "app-publish"
$fullPublish = Join-Path $workRoot "full-installer-publish"
$updatePublish = Join-Path $workRoot "update-installer-publish"
$payloadZip = Join-Path $workRoot "application-payload.zip"

function Invoke-Dotnet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

function Get-RelativeFileMap {
    param([string]$Root)

    $rootPath = (Resolve-Path $Root).Path
    $rootUri = [Uri]::new(($rootPath.TrimEnd('\') + '\'))
    $map = @{}
    foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse) {
        $relative = $rootUri.MakeRelativeUri([Uri]$file.FullName).ToString().Replace('/', '/')
        $map[$relative] = $file.FullName
    }
    return $map
}

function Add-EmbeddedPayload {
    param(
        [string]$InstallerPath,
        [string]$PayloadPath
    )

    $payloadBytes = [IO.File]::ReadAllBytes($PayloadPath)
    $markerBytes = [Text.Encoding]::ASCII.GetBytes("ZSNAPER_PAYLOAD_V1")
    $stream = [IO.File]::Open($InstallerPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $stream.Write($payloadBytes, 0, $payloadBytes.Length)
        $writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::UTF8, $true)
        try {
            $writer.Write([int64]$payloadBytes.Length)
            $writer.Write($markerBytes)
            $writer.Flush()
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $workRoot, $artifactRoot | Out-Null
if (-not $SkipBuild) {
    foreach ($publishDirectory in @($appPublish, $fullPublish, $updatePublish)) {
        if (Test-Path $publishDirectory) {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
        }
    }

    Invoke-Dotnet @(
        "publish", (Join-Path $repoRoot "ZSnaper.csproj"),
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $appPublish,
        "--nologo"
    )

    Invoke-Dotnet @(
        "publish", (Join-Path $installerRoot "src\ZSnaper.FullInstaller\ZSnaper.FullInstaller.csproj"),
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $fullPublish,
        "--nologo"
    )

    Invoke-Dotnet @(
        "publish", (Join-Path $installerRoot "src\ZSnaper.UpdateInstaller\ZSnaper.UpdateInstaller.csproj"),
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "false",
        "-p:PublishSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $updatePublish,
        "--nologo"
    )
}

if (-not (Test-Path (Join-Path $appPublish "ZSnaper.exe"))) {
    throw "Application publish output is missing ZSnaper.exe."
}
$payloadParent = Split-Path $payloadZip -Parent
New-Item -ItemType Directory -Force -Path $payloadParent | Out-Null
if (Test-Path $payloadZip) {
    Remove-Item -LiteralPath $payloadZip -Force
}
Compress-Archive -Path (Join-Path $appPublish "*") -DestinationPath $payloadZip -CompressionLevel Optimal

$setupSource = Join-Path $fullPublish "ZSnaper.FullInstaller.exe"
if (-not (Test-Path $setupSource)) {
    throw "Full installer publish output is missing ZSnaper.FullInstaller.exe."
}
$artifactNames = @(
    "ZSnaper-v$Version-$Runtime-Setup.exe",
    "ZSnaper-v$Version-$Runtime-Update.exe",
    "ZSnaper-v$Version-$Runtime-Update.zup",
    "SHA256SUMS.txt"
)
foreach ($artifactName in $artifactNames) {
    $artifactPath = Join-Path $artifactRoot $artifactName
    if (Test-Path $artifactPath -PathType Leaf) {
        Remove-Item -LiteralPath $artifactPath -Force
    }
}

$setupPath = Join-Path $artifactRoot "ZSnaper-v$Version-$Runtime-Setup.exe"
Copy-Item -LiteralPath $setupSource -Destination $setupPath -Force
Add-EmbeddedPayload -InstallerPath $setupPath -PayloadPath $payloadZip

$updateSource = Join-Path $updatePublish "ZSnaper.UpdateInstaller.exe"
if (Test-Path $updateSource) {
    Copy-Item -LiteralPath $updateSource -Destination (Join-Path $artifactRoot "ZSnaper-v$Version-$Runtime-Update.exe") -Force
}

if (-not [string]::IsNullOrWhiteSpace($BasePayloadDirectory)) {
    if (-not (Test-Path $BasePayloadDirectory -PathType Container)) {
        throw "Base payload directory was not found: $BasePayloadDirectory"
    }

    $baseMap = Get-RelativeFileMap $BasePayloadDirectory
    $newMap = Get-RelativeFileMap $appPublish
    $updateRoot = Join-Path $workRoot "update-content"
    if (Test-Path $updateRoot) {
        Remove-Item -LiteralPath $updateRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $updateRoot | Out-Null

    $changedFiles = [Collections.Generic.List[object]]::new()
    foreach ($relative in $newMap.Keys) {
        $newPath = $newMap[$relative]
        $isChanged = $true
        if ($baseMap.ContainsKey($relative)) {
            $oldHash = (Get-FileHash -LiteralPath $baseMap[$relative] -Algorithm SHA256).Hash
            $newHash = (Get-FileHash -LiteralPath $newPath -Algorithm SHA256).Hash
            $isChanged = -not [string]::Equals($oldHash, $newHash, [StringComparison]::OrdinalIgnoreCase)
        }
        if ($isChanged) {
            $destination = Join-Path $updateRoot $relative.Replace('/', '\')
            New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            Copy-Item -LiteralPath $newPath -Destination $destination -Force
            $hash = Get-FileHash -LiteralPath $newPath -Algorithm SHA256
            $changedFiles.Add([ordered]@{
                    path = $relative
                    sha256 = $hash.Hash
                    size = (Get-Item -LiteralPath $newPath).Length
                })
        }
    }

    $deletedFiles = [Collections.Generic.List[string]]::new()
    foreach ($relative in $baseMap.Keys) {
        if (-not $newMap.ContainsKey($relative)) {
            $deletedFiles.Add($relative)
        }
    }

    $manifest = [ordered]@{
        format = "zsnaper-update-1"
        from = $BaseVersion
        to = $Version
        files = $changedFiles.ToArray()
        delete = $deletedFiles.ToArray()
    }
    $manifestPath = Join-Path $updateRoot "update.manifest.json"
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $updatePath = Join-Path $artifactRoot "ZSnaper-v$Version-$Runtime-Update.zup"
    $updateZipPath = Join-Path $workRoot "update-package.zip"
    if (Test-Path $updatePath) {
        Remove-Item -LiteralPath $updatePath -Force
    }
    if (Test-Path $updateZipPath) {
        Remove-Item -LiteralPath $updateZipPath -Force
    }
    Compress-Archive -Path (Join-Path $updateRoot "*") -DestinationPath $updateZipPath -CompressionLevel Optimal
    Move-Item -LiteralPath $updateZipPath -Destination $updatePath -Force
}

$hashLines = foreach ($artifact in Get-ChildItem -LiteralPath $artifactRoot -File | Where-Object Name -ne "SHA256SUMS.txt") {
    $hash = Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $artifact.Name
}
$hashLines | Set-Content -LiteralPath (Join-Path $artifactRoot "SHA256SUMS.txt") -Encoding ASCII
Write-Host "Installer artifacts written to $artifactRoot"
