param(
    # win-x64 か win-arm64
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src/SoundDeck.App/SoundDeck.App.csproj'
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/publish'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $publishRoot $Runtime))
$zipPath = Join-Path $publishRoot "SoundDeck-$Runtime.zip"
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }

# このスクリプト専用の出力先以外は消さない
if (-not $publishDirectory.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar)) {
    throw "想定外の出力先です: $publishDirectory"
}

# 前回の発行物が混ざらないよう、出力先だけを初期化する
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

Push-Location $projectRoot
try {
    # Windows App SDK も同梱する（利用者の PC にランタイムを入れなくても起動できる）
    & dotnet publish $projectFile `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDirectory `
        --property:Platform=$platform `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish が失敗しました (終了コード $LASTEXITCODE)"
    }
}
finally {
    Pop-Location
}

$executable = Join-Path $publishDirectory 'SoundDeck.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "SoundDeck.exe が見つかりません: $executable"
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath

$sizeMiB = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)
Write-Host "発行先: $publishDirectory"
Write-Host "ZIP: $zipPath ($sizeMiB MiB)"
