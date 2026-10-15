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
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }

# 配布する exe の名前はバージョン入り（ARM64 は名前の末尾で見分ける）
$version = ([xml](Get-Content -LiteralPath $projectFile -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw "csproj から Version を読めませんでした: $projectFile"
}
$suffix = if ($Runtime -eq 'win-arm64') { '-arm64' } else { '' }
$releaseExe = Join-Path $publishRoot "SoundDeck-v$version$suffix.exe"

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
    # Windows App SDK も含めて exe 1 つにまとめる（初回起動時に %TEMP%\.net へ展開して動く）
    & dotnet publish $projectFile `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDirectory `
        --property:Platform=$platform `
        --property:PublishSingleFile=true `
        --property:IncludeAllContentForSelfExtract=true `
        --property:IncludeNativeLibrariesForSelfExtract=true `
        --property:EnableCompressionInSingleFile=true `
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

Copy-Item -LiteralPath $executable -Destination $releaseExe -Force

$sizeMiB = [Math]::Round((Get-Item -LiteralPath $releaseExe).Length / 1MB, 2)
Write-Host "発行先: $publishDirectory"
Write-Host "配布用: $releaseExe ($sizeMiB MiB)"
