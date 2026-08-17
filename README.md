# KeyDisp for Windows

**A keystroke visualizer for Windows** — [Mac 版 KeyDisp](https://github.com/con3code/keydisp) の Windows 移植版。C# / .NET 8 + WPF。

現在 MVP に向けて開発中 (未リリース)。機能仕様は Mac 版を基準とし、移植方針は
[docs/SPEC.md](docs/SPEC.md) にまとめている。

## 構成

```
├── src/
│   ├── KeyDisp.Core/            # net8.0 (OS 非依存)。状態機械・表記変換・レイアウト計算などの純粋ロジック
│   └── KeyDisp.App/             # net8.0-windows (WPF)。UI・P/Invoke フック・トレイ
├── tests/KeyDisp.Core.Tests/    # xunit。macOS/Linux 上でそのまま実行可能
└── docs/
    ├── SPEC.md                  # Mac 版から起こした移植仕様書
    └── DEVICE-TEST-CHECKLIST.md # Windows 実機でしか検証できない項目の蓄積
```

レイヤ分割の基準は「Windows 以外の OS で実行できるか否か」の一本のみ。Core は WPF / Win32 への参照を持たない。

## 開発

.NET 8 SDK があれば macOS/Linux 上でもクロスコンパイルとテストが完走する (実行は Windows のみ):

```bash
dotnet build KeyDisp.Windows.sln
dotnet test tests/KeyDisp.Core.Tests
```

CI (`.github/workflows/ci.yml`) は ubuntu でテスト、windows-latest で publish して
`KeyDisp-win-x64.zip` を artifact に生成する。`v*` タグを push すると Releases に添付される。

## Windows 上での発行

```powershell
dotnet publish src/KeyDisp.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish
```

## License

[MIT](LICENSE) — © 2026 con3code
