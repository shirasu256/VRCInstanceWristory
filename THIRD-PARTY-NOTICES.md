# 同梱物の権利表示

## OpenVR SDK

- 提供元: Valve Corporation — https://github.com/ValveSoftware/openvr
- 固定した版: **v2.15.6**（commit `0924064316de3effbcd1acf1e309182a2deb1c05`）
- 取り込んだファイル:
  - `third_party/openvr/openvr_api.cs`（C#バインディング。無編集）
  - `third_party/openvr/bin/win64/openvr_api.dll`（SHA-256: `BAB8AC6EF64E68A9CA53315B0014D131088584B2EFDFA6DB511D67EC03CFCB4A`）
- ライセンス: BSD 3-Clause（全文は `third_party/openvr/LICENSE`。配布物では実行フォルダーの `licenses/openvr/LICENSE`）

## System.Drawing.Common

- 提供元: Microsoft（NuGet パッケージ `System.Drawing.Common` 10.0.12）
- ライセンス: MIT
- 用途: 文字テクスチャの生成（GDI+）。Windows専用。

## Velopack

- 提供元: Velopack Ltd. — https://github.com/velopack/velopack
- 固定した版: **1.2.161**（NuGet パッケージ `Velopack`。インストーラーを作る道具 `vpk` も同じ版）
- ライセンス: MIT（全文は `third_party/velopack/LICENSE`。配布物では実行フォルダーの `licenses/velopack/LICENSE`）
- 用途: インストーラー・アンインストーラー・自動更新

## .NET ランタイム

- 提供元: .NET Foundation and Contributors — https://github.com/dotnet/runtime
- 版: 10（配る実行フォルダーに同梱）
- ライセンス: MIT（全文は `third_party/dotnet/LICENSE.TXT`。配布物では実行フォルダーの `licenses/dotnet/LICENSE.TXT`。ランタイムに含まれるほかの部品の表示は https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT）

## 検証にだけ使うもの

- `xunit` 2.9.3 / `xunit.runner.visualstudio` 3.1.4 / `Microsoft.NET.Test.Sdk` 17.14.1（いずれも配布物には含まれません）

## 参考にした資料（コードは取り込んでいません）

- VRChat公式のログ案内・Instancesに関するWiki
- VRCXのlocationParser（トークンの取り扱いの参考。依存はしていません）
