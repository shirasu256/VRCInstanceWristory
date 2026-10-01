# 開発とリリース

利用者向けの説明は [README.md](README.md) を見てください。ここは開発する人向けです。

## 開発用の資料について

実装メモ・仕様書・作業の約束ごと（`CLAUDE.md`）・実際の VRChat ログなどの開発用の資料は、
別の非公開リポジトリ（`shirasu256/VRCInstanceWristory-dev`）にあり、このリポジトリの `private\` フォルダーとして置いて使います。
`private\` と直下の `CLAUDE.md` は `.gitignore` で外してあります。資料がなくてもビルドと自動検証はできます。

このリポジトリには、実在の利用者の表示名・`usr_` / `wrld_` / `grp_` の ID・個人のパスを書かないでください。
検証には架空の値（`usr_00000000-0000-4000-8000-…` など）と、`tests\VRCInstanceWristory.Tests\Fixtures` の合成ログを使います。

## ビルドと確認

必要なもの: .NET 10 SDK、PowerShell 7（`pwsh`）

```powershell
pwsh scripts\publish.ps1
```

自動検証（`dotnet test -c Release`）→ `dist\VRCInstanceWristory` への publish → dist の実行ファイルでの描画の確認、までを行います。
見た目を変えたときは `-UpdateDocs` を付けると、`docs\ui` の画像も dist の実行ファイルで作り直します。

- 開発中の確認は `dist\VRCInstanceWristory\VRCInstanceWristory.exe` で行います（`src\...\bin\...` ではなく）。
- dist はインストーラーで入れた版ではないので、アップデートの機能は使えません（グレーアウトします）。
- インストーラーで入れた版（`%LocalAppData%\VRCInstanceWristoryApp`）が起動していても publish できます。dist の実行ファイルが起動しているときだけ止まります。

## 構成

| 場所 | 中身 |
| --- | --- |
| `src\VRCInstanceWristory` | アプリ本体 |
| `src\VRCInstanceWristory.OpenVR` | OpenVR のバインディング |
| `tests\VRCInstanceWristory.Tests` | 自動検証（xunit） |
| `scripts\publish.ps1` | 開発用の publish（dist）と描画の確認 |
| `scripts\package.ps1` | 配るインストーラーを作る（Velopack の `vpk pack`） |
| `.github\workflows\release.yml` | 版のタグを push したときに、インストーラーを作って Releases へ公開する |
| `dotnet-tools.json` | `vpk` の版（アプリが使う `Velopack` パッケージと同じ版にそろえる。自動検証で確かめている） |
| `docs\ui` | 画面の見本の画像（`--render-sample` で作る）と手書きのモックアップ |

## 配り方

インストーラー・アンインストーラー・自動更新は [Velopack](https://velopack.io/) で作ります。

| 事項 | 内容 |
| --- | --- |
| インストール先 | `%LocalAppData%\VRCInstanceWristoryApp`（管理者の権限は要らない。ショートカットはスタートメニューだけ） |
| 設定と訪問履歴 | `%LocalAppData%\VRCInstanceWristory`。**インストール先と同じ名前にしない**（Velopack はインストールし直すときにインストール先をまるごと消すため） |
| アンインストール | Windows の「アプリ」から。インストール先・設定と訪問履歴・自動起動の登録を消す |
| 自動更新 | アプリが GitHub の Releases（正式な版だけ）を確かめ、「アプリを更新」を押すと入れ替えて起動し直す |

手元でインストーラーを試すときは `pwsh scripts\package.ps1` を実行すると、`artifacts\releases` にできます。
できた `VRCInstanceWristoryApp-win-Setup.exe` を開くと、この PC に実際にインストールされます。

## リリースの手順

1. `Directory.Build.props` の `<Version>` を上げます（例 `0.1.2`）。版はアプリの表示（`v0.1.2`）とタグにそのまま使います。
2. `pwsh scripts\publish.ps1 -UpdateDocs` が通ることを確かめます（版が変わると `docs\ui` の画像の版の表記も変わります）。
3. 変更をコミットして `main` へ push します。
4. 同じ版のタグを push します。

   ```powershell
   git tag -a v0.1.2 -m "VRC Instance Wristory v0.1.2"
   git push origin v0.1.2
   ```

5. GitHub Actions の「Release」が終わるのを待ちます（`gh run watch`）。自動検証・前の版の取得（差分の更新を作るため）・インストーラーの作成・Releases への公開まで行います。
   自動検証が通らなければ公開しません。そのときは直してタグを付け直します（`git tag -d v0.1.2`・`git push origin :refs/tags/v0.1.2`）。
6. リリースノートを書きます。Actions はリリースノートを書かないので、手で入れます。

   ```powershell
   gh release edit v0.1.2 --notes-file release-notes.md
   ```

7. Releases に `VRCInstanceWristoryApp-win-Setup.exe`・`-full.nupkg`・`-delta.nupkg`・`releases.win.json` があることを確かめます。
8. 前の版を入れた PC で「アプリを更新」が出て、更新して起動し直せることを確かめます。

公開した版は、インストールしたアプリが自動更新で見つけます。取り消しにくいので、タグを push する前に確かめてください。
