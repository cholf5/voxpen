[CmdletBinding()]
param(
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Get-DevelopmentVersion {
    $projectFile = 'src/VoxPen.App/VoxPen.App.csproj'
    $projectContent = Get-Content -Raw $projectFile
    $match = [regex]::Match($projectContent, '<Version>([^<]+)</Version>')
    $baseVersion = if ($match.Success) { $match.Groups[1].Value } else { '0.1.0' }

    if ($baseVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "应用项目的 Version '$baseVersion' 必须是三段数字版本号。"
    }

    return "$baseVersion-dev.$(Get-Date -Format 'yyyyMMddHHmmss')"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DevelopmentVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "版本号 '$Version' 不符合 SemVer 格式（如 0.1.0 或 0.1.0-rc.1）。"
}

$numericVersion = $Version -replace '-.*', ''
$stageName = "VoxPen-$Version-win-x64"
$publishDirectory = 'publish/win-x64'
$stageDirectory = Join-Path 'staging' $stageName
$zipPath = "$stageName.zip"
$sha256Path = "$zipPath.sha256"

Write-Host "打包版本：$Version"

& dotnet publish src/VoxPen.App/VoxPen.App.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$numericVersion.0 `
    -p:FileVersion=$numericVersion.0 `
    -p:InformationalVersion=$Version `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

if (Test-Path $stageDirectory) {
    Remove-Item -LiteralPath $stageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

Copy-Item (Join-Path $publishDirectory 'VoxPen.App.exe') $stageDirectory
if (Test-Path (Join-Path $publishDirectory 'hot-rule.txt')) {
    Copy-Item (Join-Path $publishDirectory 'hot-rule.txt') $stageDirectory
} elseif (Test-Path 'hot-rule.txt') {
    Copy-Item 'hot-rule.txt' $stageDirectory
}
if (Test-Path 'hot.txt') { Copy-Item 'hot.txt' $stageDirectory }
if (Test-Path 'config.sample.json') { Copy-Item 'config.sample.json' $stageDirectory }
Copy-Item 'README.md' $stageDirectory
Copy-Item 'FIRST-RUN.txt' $stageDirectory

Compress-Archive -Path $stageDirectory -DestinationPath $zipPath -Force
$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
Set-Content -LiteralPath $sha256Path -Value "$hash  $zipPath" -Encoding ascii

Get-ChildItem $stageDirectory | Format-Table -AutoSize
$size = (Get-Item $zipPath).Length / 1MB
Write-Host ("Zip size: {0:N1} MB" -f $size)
Write-Host "SHA256: $hash"
Write-Host "已生成：$zipPath、$sha256Path"
