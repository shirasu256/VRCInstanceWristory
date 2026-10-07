<#
.SYNOPSIS
    変更を dist\VRCInstanceWristory まで反映し、dist の実行ファイル自身で見た目を確かめる。

.DESCRIPTION
    開発中の確認は dist\VRCInstanceWristory の実行ファイルで行う（利用者に試してもらうときも dist）。
    dotnet build / dotnet test が通っても dist が古いままなら実機の動作は変わらないので、
    コードを変えたら報告の前にこれを1回実行する。
    配るインストーラーは scripts\package.ps1、公開は版のタグを push したときの GitHub Actions（.github\workflows\release.yml）。

      1. dist の VRCInstanceWristory が起動中でないことを確かめる（起動中はファイルを掴んでいて上書きに失敗する。インストールした版は構わない）
      2. dotnet test（配る構成と同じ Release で）
      3. dotnet publish を一時フォルダーへ出し、dist\VRCInstanceWristory をその中身と同じにする
         （上書きだけでは、消した・名前を変えたファイルが dist に残り続けるため）
      4. dist\VRCInstanceWristory\VRCInstanceWristory.exe --render-sample で確認用の画像を出す
         （手首のパネル、--window のデスクトップのウィンドウ、--dashboard のSteamVRのダッシュボードの3枚。
         どれもSteamVRには触れない）
      5. -UpdateDocs を付けたときは、docs\ui の見本の画像をすべて dist の実行ファイルで作り直す

.EXAMPLE
    pwsh scripts/publish.ps1

.EXAMPLE
    pwsh scripts/publish.ps1 -UpdateDocs    # 見た目を変えたとき（docs\ui の画像も作り直す）

.EXAMPLE
    pwsh scripts/publish.ps1 -SkipTests     # 直前に dotnet test を通してあるとき
#>
[CmdletBinding()]
param(
    # 自動検証を飛ばす。直前に dotnet test を通してあるときだけ使う。
    [switch] $SkipTests,

    # 最後の --render-sample を飛ばす。見た目に関わらない変更のときだけ使う。
    [switch] $SkipVerify,

    # docs\ui の見本の画像（CLAUDE.md 3節の一覧）を、dist の実行ファイルで作り直す。
    [switch] $UpdateDocs,

    # 確認用の画像の保存先のフォルダー。既定は一時フォルダー。
    [string] $VerifyDirectory
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist\VRCInstanceWristory'
$exe = Join-Path $dist 'VRCInstanceWristory.exe'

# --render-sample で1枚描き、ファイルができたことまで確かめる（終了コードが0でも書けていないことがあるため）。
function Invoke-RenderSample {
    param([string] $Path, [string[]] $Options = @())

    $started = Get-Date
    # 実行ファイルは Windows のアプリなので、パイプでつながないと PowerShell は終わりを待たない（→実装メモ5.63）。
    & $exe --render-sample $Path @Options | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "--render-sample $($Options -join ' ') が失敗しました。" }

    $file = Get-Item -LiteralPath $Path -ErrorAction Ignore
    if ($null -eq $file -or $file.LastWriteTime -lt $started.AddSeconds(-1)) {
        throw "--render-sample $($Options -join ' ') の画像ができていません: $Path"
    }
}

Push-Location $root
try {
    # dist の実行ファイルが起動中なら止める（ファイルを掴んでいて上書きに失敗する）。
    # インストーラーで入れた版（%LocalAppData%\VRCInstanceWristoryApp）は dist を掴まないので、起動していてもよい（→実装メモ5.124）。
    $running = @(Get-Process -Name 'VRCInstanceWristory' -ErrorAction Ignore | Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -gt 0) {
        $ids = ($running | ForEach-Object { $_.Id }) -join ', '
        throw "dist の VRCInstanceWristory が起動中です（PID: $ids）。終了してから実行してください。"
    }

    if (-not $SkipTests) {
        Write-Host '== dotnet test（Release） ==' -ForegroundColor Cyan
        dotnet test -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'dotnet test が失敗しました。dist は更新していません。' }
    }

    $staging = Join-Path ([System.IO.Path]::GetTempPath()) "vrcinstancewristory-publish-$PID"
    if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }

    try {
        Write-Host '== dotnet publish ==' -ForegroundColor Cyan
        dotnet publish src\VRCInstanceWristory -c Release -o $staging --nologo
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish が失敗しました。dist は更新していません。' }

        Write-Host '== dist\VRCInstanceWristory を publish の中身と同じにする ==' -ForegroundColor Cyan
        robocopy $staging $dist /MIR /NFL /NDL /NJH /NJS /NP | Out-Host
        # robocopy の終了コードは 8 未満が成功（1 は「複写した」、2 は「余分なファイルを消した」など）。
        if ($LASTEXITCODE -ge 8) { throw "dist を更新できませんでした（robocopy の終了コード $LASTEXITCODE）。" }
        $global:LASTEXITCODE = 0
    }
    finally {
        if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }

    if (-not $SkipVerify) {
        if ([string]::IsNullOrWhiteSpace($VerifyDirectory)) {
            $VerifyDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'vrcinstancewristory-dist-check'
        }

        New-Item -ItemType Directory -Force -Path $VerifyDirectory | Out-Null
        $panel = Join-Path $VerifyDirectory 'panel.png'
        $window = Join-Path $VerifyDirectory 'window.png'
        $dashboard = Join-Path $VerifyDirectory 'dashboard.png'

        Write-Host '== dist の実行ファイルで描画を確認 ==' -ForegroundColor Cyan
        Invoke-RenderSample $panel
        Invoke-RenderSample $window @('--window')
        Invoke-RenderSample $dashboard @('--dashboard')
    }

    if ($UpdateDocs) {
        Write-Host '== docs\ui の見本の画像を dist の実行ファイルで作り直す ==' -ForegroundColor Cyan
        $ui = Join-Path $root 'docs\ui'

        # CLAUDE.md 3節の一覧と同じもの。見本を足したら両方へ足す。
        $samples = @(
            @{ File = 'render-sample-history.png'; Options = @() },
            @{ File = 'render-sample-mark.png'; Options = @('--mark-popup') },
            @{ File = 'render-sample-reset-confirm.png'; Options = @('--reset-confirm') },
            @{ File = 'render-sample-countdown-glow.png'; Options = @('--countdown-glow') },
            @{ File = 'render-sample-countdown-warning.png'; Options = @('--countdown-warning') },
            @{ File = 'render-sample-window.png'; Options = @('--window') },
            @{ File = 'render-sample-window-panel.png'; Options = @('--window', '--tab', 'panel') },
            @{ File = 'render-sample-window-startup.png'; Options = @('--window', '--tab', 'startup') },
            @{ File = 'render-sample-window-vr-error.png'; Options = @('--window', '--vr-error') },
            @{ File = 'render-sample-dashboard.png'; Options = @('--dashboard') },
            @{ File = 'render-sample-reset-warning.png'; Options = @('--reset-warning') },
            @{ File = 'render-sample-welcome.png'; Options = @('--welcome') },
            @{ File = 'render-sample-welcome-setup.png'; Options = @('--welcome', '--setup') }
        )

        foreach ($sample in $samples) {
            Invoke-RenderSample (Join-Path $ui $sample.File) $sample.Options
        }
    }

    Write-Host ''
    Write-Host "dist を更新しました: $dist" -ForegroundColor Green

    if (-not $SkipVerify) {
        Write-Host "確認用の画像: $VerifyDirectory（panel.png / window.png / dashboard.png）" -ForegroundColor Green
        Write-Host 'これらの画像は dist の実行ファイル自身が描いたものです。変更が見た目に出ているか開いて確かめてください。'
    }

    if ($UpdateDocs) {
        Write-Host 'docs\ui の見本の画像を作り直しました。docs\ui\wrist-history.html（手書きのモックアップ）は手で合わせてください。' -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
