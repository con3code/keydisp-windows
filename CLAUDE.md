# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

KeyDisp for Windows — [Mac 版 KeyDisp](https://github.com/con3code/keydisp) の Windows 移植 (C# / .NET 8 + WPF)。押したキーとマウス操作を透明オーバーレイにリアルタイム表示する。UI 文言・コードコメントは日本語が基本。

**Mac 版の挙動が仕様の正**。移植仕様と Windows 差分の判断は `docs/SPEC.md`、実機でしか検証できない項目は `docs/DEVICE-TEST-CHECKLIST.md` に蓄積する。開発環境が /Dev/keydisp/windows (Mac 版リポジトリのコピーの中の入れ子リポ) の場合、参照用の Mac 版 Swift ソースが `../KeyDisp/Sources/` にある。

## ビルドとテスト

.NET 8 SDK で macOS/Linux 上でもクロスコンパイル+テストが完走する (実行は Windows のみ)。macOS 開発機では SDK が `~/.dotnet` にある:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build KeyDisp.Windows.sln
dotnet test tests/KeyDisp.Core.Tests
```

実行確認は CI (windows-latest の publish) と実機テスト日にまとめて行う。**Core の単体テストが品質の生命線** — 状態機械の変更には必ずシナリオテストを付けること。

## アーキテクチャ

レイヤ分割の基準は「Windows 以外でも実行できるか」の一本のみ:

- **KeyDisp.Core** (net8.0 プレーン、WPF/Win32 参照禁止):
  - `StateMachine/KeyStateMachine` — 入力→表示行の状態機械 (Mac 版 KeyCaptureController の移植)。タイピング連結・×n 差し戻し・修飾キーピーク保持・押しっぱなし判定・reconcile
  - `Display/KeyDisplayModel` — 行ライフサイクル (active→holding→fading→削除)、freeze
  - `Formatting/KeyFormatter` — VK→トークン変換。内部正準トークンは Mac 記号で、表示時に osLabelStyle (既定 Windows) で写像
  - `Layout/OverlayMetrics` — 実測ベースの折り返し・visibleRows。幅測定は `ITextMeasurer` 注入
  - 時間は `IDelayScheduler` で抽象化。テストは `VirtualScheduler` (tests/TestSupport) で決定的に進める
- **KeyDisp.App** (net8.0-windows、WPF): UI・低レベルフック (P/Invoke)・トレイ・設定 I/O

設定は `%APPDATA%\KeyDisp\settings.json` (スキーマは `Settings/SettingsDocument`、キー名は Mac 版 UserDefaults 踏襲)。ローカライズは `.resx` ではなく `L(ja, en)` ヘルパー方式。

## 重要な制約

- 低レベルフックのコールバックは即 return が鉄則 (LowLevelHooksTimeout でフックが外される)。重い処理は Channel 経由で UI スレッドへ
- 折り返し判定は計測と配置で必ず OverlayMetrics の同じ関数を使う (Mac 版 FlowLayout の二重判定バグの教訓)
- 寸法係数は Mac 版 (SF Rounded) の仮置き値。実機調整はまとめて行うので勝手に散らさない
- Windows にはセキュア入力保護が無い (パスワード欄でも取れる)。showAllKeys の既定オフを崩さないこと
