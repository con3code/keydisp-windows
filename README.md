# KeyDisp for Windows

**A keystroke visualizer for Windows** — [Mac 版 KeyDisp](https://github.com/con3code/keydisp) の Windows 移植版。C# / .NET 8 + WPF。

押したキー・ショートカット・マウス操作を画面上に大きくリアルタイム表示します。
授業・研修・プレゼン・スクリーンキャスト・ペアプログラミング向け。

現在プレリリース開発中。機能仕様は Mac 版を基準とし、移植方針と Windows 差分は
[docs/SPEC.md](docs/SPEC.md) にまとめています。

## 主な機能 (実装済み)

- **リアルタイムキー表示** — 押している間は表示を維持し、離すと保持時間ののちフェードアウト (時間は調整可)
- **修飾キー・特殊キー・コンビネーション** — Ctrl/Alt/Shift/Win、矢印、F1-F24、メディアキーとその組み合わせ。連打・長押しは「Ctrl+V ×3」のようにまとめてパルス表示
- **3 つの表示スタイル** — シンプル / キーキャップ風 / カスタム背景画像 (ナインパッチ)。色・濃さ・文字縁取り調整可
- **表記スタイル** — Windows 表記 (既定) / Mac 記号 / 併記 (Ctrl/⌘)。Mac ユーザーが混ざる場でも伝わる表示に
- **マウス可視化** — クリック中のカーソルハイライト (左右で見た目が変わる)、大きいポインタの重ね表示
- **表示編集モード** — 破線枠のドラッグで移動・端ドラッグでリサイズ・画面中心スナップ。HUD でスタイルをライブ調整
- **マルチモニタ** — カーソルのある画面への追従、モニタごとの位置・表示プロファイル記憶 (Per-Monitor DPI v2 対応)
- **その他** — 表示中のキーを掴んで移動 (dragToMove)、ホットエッジ (上端で凍結 / 下端で一時非表示)、グローバルショートカット (既定 Alt+Win+K)、ログイン時起動、日英 UI

## 使い方

1. `KeyDisp.exe` を起動するとトレイに常駐します (ウィンドウは出ません)
2. ショートカットや特殊キーを押すと画面に表示されます。通常のタイピングも表示するには、トレイメニューまたは設定の「**すべてのキー入力を表示**」をオンにします
3. 表示の位置・見た目は、トレイメニューの「**表示編集モード**」で調整します
4. 詳細設定はトレイメニューの「**設定…**」から
5. 表示のオン/オフは **Alt+Win+K** (設定で変更可)

## プライバシー

KeyDisp は入力を画面に表示するだけで、**記録も送信も一切しません** (ネットワークアクセスはありません)。
Windows にはパスワード入力欄を保護する仕組みが無いため、「すべてのキー入力を表示」がオンの間は
パスワードも画面に表示され得ます。人に画面を見せる前にショートカットで表示を切ってください。

## 既知の課題

- かな入力 (JIS かな配列) 表示と IME 切替キーの表記は見直し中です ([docs/SPEC.md §9.5](docs/SPEC.md))
- 管理者権限で動いているアプリにフォーカスがある間は、キーが表示されません (Windows のセキュリティ仕様)

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
