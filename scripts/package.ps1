<#
.SYNOPSIS
    配るインストーラー（Velopack）を作る（→実装メモ5.121）。

.DESCRIPTION
    1. dotnet publish を一時フォルダーへ出す（.NET のランタイムを同梱した実行フォルダー→5.119）
    2. -DownloadPrevious を付けたときは、GitHub の Releases から前の版を落とす（差分の更新を作るため）
    3. vpk pack で artifacts\releases にインストーラーと更新の材料を作る
       （ショートカットはスタートメニューだけ。デスクトップには作らない→実装メモ5.124）

    できるもの（artifacts\releases）:
      VRCInstanceWristoryApp-win-Setup.exe      配るインストーラー（利用者はこれを開く）
      VRCInstanceWristoryApp-win-Portable.zip   インストールしない形（展開して使う）
      VRCInstanceWristoryApp-<版>-full.nupkg     更新の材料（アプリが自動更新で落とす）
      VRCInstanceWristoryApp-<版>-delta.nupkg    前の版との差分（-DownloadPrevious のときだけ）
      releases.win.json ほか                    更新の一覧

    GitHub の Releases へ上げるのは、タグを push したときの GitHub Actions（.github\workflows\release.yml）。
    手元でこのスクリプトを使うのは、インストーラーを試すときだけ。
    作ったインストーラーを開くとこのPCに実際にインストールされる（%LocalAppData%\VRCInstanceWristoryApp）。

.EXAMPLE
    pwsh scripts/package.ps1

.EXAMPLE
    pwsh scripts/package.ps1 -Version 0.2.0 -DownloadPrevious -Token $env:GITHUB_TOKEN
#>
[CmdletBinding()]
param(
    # 作る版。省くと Directory.Build.props の <Version>。
    [string] $Version,

    # 前の版を GitHub の Releases から落として、差分の更新も作る。
    [switch] $DownloadPrevious,

    # 公開するリポジトリ（アプリの AppInfo.RepositoryUrl と同じ）。
    [string] $RepoUrl = 'https://github.com/shirasu256/VRCInstanceWristory',

    # GitHub のトークン（前の版を落とすとき。公開リポジトリなら省いてもよいが、回数の制限がゆるくなる）。
    [string] $Token
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

# インストール先の名前。保存先（%LocalAppData%\VRCInstanceWristory）と同じにしない（AppInfo.PackageId と同じ値→5.121）。
$packId = 'VRCInstanceWristoryApp'
$output = Join-Path $root 'artifacts\releases'

if (-not $Version) {
    [xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
}

if (-not $Version) { throw 'Directory.Build.props の <Version> を読めません。-Version で指定してください。' }

Push-Location $root
try {
    # dotnet の出力はパイプでつながず、そのまま画面へ出す（パイプでつなぐと PowerShell が文字コードを読み違えて日本語が化ける）。
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore が失敗しました。' }

    $publish = Join-Path ([System.IO.Path]::GetTempPath()) "vrcinstancewristory-package-$PID"
    if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }

    Write-Host "== dotnet publish（v$Version） ==" -ForegroundColor Cyan
    dotnet publish src\VRCInstanceWristory -c Release -o $publish -p:Version=$Version --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish が失敗しました。' }

    # 不具合を調べるための pdb は配らない（インストーラーが大きくなるだけ）。
    Get-ChildItem -LiteralPath $publish -Filter *.pdb | Remove-Item -Force

    # 前に作ったものは消してから作る。同じ版が残っていると vpk pack が断る（前の版は -DownloadPrevious で GitHub から取り直す）。
    if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    New-Item -ItemType Directory -Force $output | Out-Null

    if ($DownloadPrevious) {
        Write-Host '== 前の版を落とす（差分の更新を作るため） ==' -ForegroundColor Cyan
        $download = @('vpk', 'download', 'github', '--repoUrl', $RepoUrl, '--outputDir', $output)
        if ($Token) { $download += @('--token', $Token) }
        dotnet @download
        if ($LASTEXITCODE -ne 0) { Write-Warning '前の版を落とせませんでした（最初の版なら問題ありません）。差分なしで作ります。' }
    }

    Write-Host '== vpk pack ==' -ForegroundColor Cyan
    dotnet vpk pack `
        --packId $packId `
        --packVersion $Version `
        --packDir $publish `
        --mainExe 'VRCInstanceWristory.exe' `
        --runtime 'win-x64' `
        --packTitle 'VRC Instance Wristory' `
        --packAuthors 'shirasu256' `
        --icon (Join-Path $root 'src\VRCInstanceWristory\app.ico') `
        --shortcuts 'StartMenuRoot' `
        --outputDir $output
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack が失敗しました。' }

    Write-Host ''
    Write-Host "インストーラーを作りました: $output" -ForegroundColor Green
    Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object { '  {0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) } | Out-Host
}
finally {
    if ($publish -and (Test-Path $publish)) { Remove-Item -LiteralPath $publish -Recurse -Force }
    Pop-Location
}
