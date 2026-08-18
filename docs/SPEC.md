# KeyDisp for Windows — 移植仕様書

Mac 版 (リポジトリルート `KeyDisp/Sources/`) の棚卸しから起こした移植仕様。
「Mac 版の挙動が正」であり、本書は Windows での差分判断を含む。参照は Swift ソースの行番号。

## 0. 移植の大方針

- キー表示状態機械・行ライフサイクル・折り返し計算は **Mac 版を忠実に移植**し、単体テストで固める
- Windows で意味を持たない機能 (fn/🌐、権限ガイド等) は移植しない
- 設定キー名は Mac 版 UserDefaults を camelCase で踏襲 (`%APPDATA%\KeyDisp\settings.json`)

## 1. 行のライフサイクル (KeyDisplayModel.swift)

- `KeyEntry`: tokens / isTyping / phase(active→holding→fading) / count(×n)
- `scheduleFade`: 離してから `holdDuration`(既定1.5s) 後に fading、`hold + max(0.05, fadeDuration) + 0.1` 後に削除
- `trimRows`: `maxRows`(既定4、1-8) 超過で**古い方(先頭)から**削除
- **フリーズ** (`FreezeReason`: topEdge / dragging): 立っている間は消去予定を全取消し、fading 行は holding に戻す。解除時に再スケジュール
- `releaseOtherTypingRows`: タイピング行は同時に 1 つだけ
- `decrement` は `max(1, count-1)`。`update` でトークンが変わると count は 1 にリセット

## 2. 入力状態機械 (KeyCaptureController.swift)

### 定数
- タイピング連結窓 `typingAppendWindow = 1.2s` / `maxTypingTokens = 400`
- 押しっぱなし判定 `deliberateHoldDelay` = 設定 `holdJudgeDelay`(既定 0.5s、0.2-2.0)

### 保持状態
`pressedKeys` / `currentID` / `currentIsModifierOnly` / `currentModifiers` / `modifierPeak`(操作中の修飾キー最大集合。片方離しても表示維持) / `modifierPressOrder` / `suppressModifierEntry` / `mouseEntryID` / `lastComboID+Tokens` / `lastArrowID+Time` / `lastTypingID+Time`

### keyDown の分岐 (`:490-663`)
1. **autorepeat**: `countRepeats` オン・修飾単独行でない・タイピング行でない・noRepeat キー(Mac: 英数/かな → Win: 無変換/変換/半角全角)でないときのみ increment。それ以外は無視
2. タイピング判定: 文字キーかつ (flags 空 or Shift のみ)。Space(flags 空/Shift) は連結中なら区切らず連結
3. タイピング経路: 直前が ×n 付き修飾単独行なら**差し戻し**(decrement→release)。`showAllKeys` オフなら非表示 (差し戻しは行う)。1.2s 以内かつ幅に収まれば append、他のタイピング行は release
4. コンボ経路: トークン = 修飾キー列 + キーラベル。直前が修飾単独行なら 3 分岐:
   - 同一トークンの連続 → 単独行を remove して既存行 increment
   - ×n 付き → decrement + release(履歴化) してコンボ新規行 (**⌘×3→⌘A パターン**)
   - それ以外 → update で「⌘」→「⌘C」に書き換え
5. 単独行がなければ `mergeTargetID` 取得時 increment、なければ従前行 release + 新規行

### mergeTargetID (×n の条件)
`countRepeats` オン ∧ トークン列が `lastComboTokens` と完全一致 ∧ その行が生存 ∧ **entries の最後** (間に行が挟まったらマージしない)

### armRowNarrow (押しっぱなし判定)
`deliberateHoldDelay` 後発火のキャンセル可能遅延。発火条件: `currentID` あり ∧ count==1。
- `stepModifierRelease` オン: 現在行 release + 残りで新規行 (⌥⇧⌘→⇧⌘→⇧ と行が増える)
- オフ: update で同じ行を狭める
- **早く離しきったらキャンセルされ、押した組み合わせ全体が 1 行として残る** (中核ルール)

### flagsChanged 相当 (Windows: 修飾キーの down/up から合成)
- flags 空になった → peak/order クリア。通常キー押下中なら armRowNarrow、でなければ lastCombo 記録して release
- 減った (`!flags.isSuperset(of: peak)`) → armRowNarrow に委ねる (ちらつき防止)
- 増えた → `peak.formUnion` して update。**離したぶんは消さない**
- currentID なし: タイピング連結中の Shift 単独は行を出さない。Alt/Shift だけならタイピング連結を切らない
- Caps Lock: Mac はトグルの flagsChanged → **Windows は通常の keydown/keyup**。「begin→即 release」のフラッシュ表示 + ×n は同じ挙動に合わせる

### 矢印グルーピング (`arrowGrouping`: 0=同時のみ/1=連続も/2=なし)
修飾キーなしの矢印のみ対象。join 先が ×n 済みなら decrement+release して結合新規行 (「→×4」が「→↓×4」に化けるのを防ぐ)。join 可なら append。

### reconcile (取り残し回収)
1s 周期 + イベント処理のたび。`GetAsyncKeyState` 相当で実際の押下状態と突き合わせ、`currentID`/`mouseEntryID` 以外の active 行はすべて release (順序非依存)。非表示中 (overlayVisible オフ / ホットエッジ抑制) はキー処理をスキップし状態リセット。

### マウス + キー行
`showClickInKeyDisplay` オン時のみ。**単独クリックは行にしない** (修飾キー or 押しっぱなし文字キーがあるときだけ)。押下文字キーがタイピング行として表示済みなら新規行を作らず update (二重表示防止)。MouseUp で修飾も押下キーも無ければ release。

### Windows で削除するもの
- fn/🌐 遅延一式 (`pendingLoneFn`, `removeTrailingLoneFnRow`, `loneFnDisplayDelay`) — fn は OS に届かない
- 暗黙 fn 除去 (`implicitFnKeys`) — 同上
- メディアキーの NX_SYSDEFINED 解釈と擬似コード 1000 番台 — VK_VOLUME_UP 等が普通に届く
- セキュア入力対応の注記 — 逆に **Windows は保護が無い**ため、プライバシー設計 (§8) が必要

### Windows で追加するもの
- **リピート判定の自前合成**: KBDLLHOOKSTRUCT にリピートフラグが無い → `pressedKeys` に既に居る VK の keydown = autorepeat
- **フック再インストール**: LowLevelHooksTimeout でフックが外される → watchdog + SessionSwitch/PowerModeChanged で再設置

## 3. キー表記変換 (KeyFormatter.swift)

- 特殊キー表を VK ベースで再構築 (Enter/Tab/Space/BackSpace/Esc/CapsLock/矢印/F1-F24/Home/End/PgUp/PgDn/Del/Ins、無変換/変換/半角全角/カタカナひらがな)
- メディアキー: VK_VOLUME_MUTE/DOWN/UP、VK_MEDIA_* をラベル表示 (Mac の F 番号写像は不採用、Windows 流のラベルに)
- **表記スタイルの意味が反転**: 既定 = Windows ネイティブ表記。オプションで Mac 記号 / 併存 (`Ctrl/⌘`)。対応表は Mac 版 `windowsLabels` (⌘→Ctrl はショートカット互換の対応) を逆向きに利用
- 修飾キー表示順: Windows 慣例 Ctrl → Alt → Shift → Win (Mac: fn⌃⌥⇧⌘)。`modifierPressOrder` オンで押下順
- Ctrl+⌘ 同時の Ctrl 重複除去は Mac 表記選択時のみ意味を持つ
- 文字キー: `ToUnicodeEx` (状態非破壊フラグ 0x4、Win10 1809+)。既定は大文字化、`distinguishCase` オンで Shift/CapsLock 状態を渡して実入力どおり。コンビネーションは常に大文字
- JIS かな配列表: Windows の JIS 配列スキャンコードで引き直し (`゛゜「」` の位置が Mac と一部異なる点に注意)。`kanaDisplay` は手動オプション + IMM32 (`ImmGetOpenStatus`) で日本語入力モード判定
- ⌥記号併記 (`showOptionSymbols`) は**移植対象外** (AltGr 対応は将来検討)
- クリックトークン `«click»/«rclick»/«mclick»` → 描画時にアイコン置換 (Segoe Fluent Icons / 自前 Path)
- 🌐 は Windows に対応キーなし。`globeOnImeKeys` は IME 切替キー (半角全角等) への装飾として存続を検討 (既定オフに変更)

## 4. 折り返し・寸法計算 (OverlayRootView.swift の OverlayMetrics)

- rowHeight: simple 58×s / keycap 56×s / customImage 64×s。extraLineHeight: 41/55/41×s。rowSpacing 8×s
- 幅は**実測** (`ITextMeasurer` 注入。App は FormattedText、テストはフェイク)。トークン単位キャッシュ
- keycap 幅 = `max(38, textWidth) + 14 + 5` (フォント 30×s bold)
- タイプライター式 (keycap/customImage): `typingLineFits` で収まるかを実測判定。simple は折り返しに任せる
- `visibleRows`: 新しい行を優先、収まらない古い行から落とす。1 行でも収まらなければ二分探索で先頭(古い文字)を削る
- 行内の区切り: `+`(plusSeparator 時) / タイピング=ZWSP / コンボ=thin space+ZWSP
- **教訓**: 計測と配置で同じ幅判定を使うこと (FlowLayout の二重判定バグ、DEVLOG:388-405)
- フォント: Segoe UI (Semibold/Bold) 基準。寸法係数は `OverlayConstants.cs` に集約し実機日に一括調整

## 5. オーバーレイウィンドウ (OverlayWindowController.swift)

- WPF: AllowsTransparency + `WS_EX_TOOLWINDOW|NOACTIVATE|TRANSPARENT` + TOPMOST。新規行が入った瞬間に SetWindowPos で TOPMOST 再主張。行が空なら Hide()
- 表示条件: `editMode || (wantsVisible && entries.Any() && !hiddenOnCurrentScreen)`。編集モード中は必ず表示
- マウス受付 3 モード: 編集モード=全域受付 / dragToMove=行矩形 (±8px) 上のみ受付 (LL マウスフックの座標で判定し WS_EX_TRANSPARENT を動的切替) / 通常=完全透過
- ドラッグ検出: `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` (Mac 版の mouseUp ポーリングは不要)。ドラッグ中は freeze(dragging) + 画面別フレーム保存を保留。終了時に保存 + 画面をまたいだら移動先プロファイルへ `adoptProfile`
- 中心スナップ: 編集モード中のドラッグのみ、画面中心から 10px 以内で縦横それぞれ吸着 + アンバー破線ガイド (別ウィンドウ、dash [6,5])
- 既定サイズ 620×440、最小 240×150。`growToFitContent`: scale/maxRows/style 変化時に足りない分だけ広げる (手動拡大は縮めない)。stackFromTop で伸ばす方向が変わる
- カーソル画面追従 (`followCursorScreen`): タイマーでなく**新規行が入った瞬間**に判定。編集中・ドラッグ中は動かさない。記憶フレームがあれば復元、なければ相対位置比率で remap → clamp。**フレームを移した後に** adoptProfile
- 画面別プロファイル (10 項目): style, displayScale, maxRows, stackFromTop, rowAlignment, textColorHex, keyColorHex, backgroundEnabled, backgroundOpacity, hidden。150ms デバウンス保存、適用中の再入ガード
- モニタ安定 ID: `QueryDisplayConfig` の monitorDevicePath (EDID 由来)、同型番衝突は出現順サフィックス、失敗時 `\\.\DISPLAYn`
- 座標系: 物理 px + Win32 API で統一。DIP 変換は境界 1 箇所。PMv2 マニフェスト必須

## 6. 描画スタイル (OverlayRootView.swift)

- 共通: fontSize 34×s、`bgOpacity = backgroundEnabled ? backgroundOpacity : 0`
- **simple**: 連結テキスト、padding 14×s/7×s、角丸 14×s の背景
- **keycap**: FlowLayout にトークンごとのキーキャップ。下段 Border(3.5×s オフセット、-18% 減光)=厚み + 上段グラデ (opacity→×0.85) + 白 22% 1px 枠。文字 30×s bold、minWidth 38×s。縁取りは適用しない。背景オフ時も opacity 0.15 を維持。影は DropShadowEffect を使わずフェイク描画
- **customImage**: ナインパッチ (1/3 切り、潰れ防止クランプ) を OnRender で 9 領域 DrawImage。読めなければ角丸単色にフォールバック。表示高さは 1 行ぶんに正規化
- 文字縁取り (`textOutline`): Geometry 2 回描画 (背面 Stroke + 前面 Fill)。Mac 版の 8 方向影ハックより高品質
- アニメーション: 自作 SpringEase(response, dampingFraction) — 挿入 spring(0.28, 0.85)、×n パルス scale 1.1→1 spring(0.25, 0.55) anchor 左下 + 0.12s 間引き、フェードは fadeDuration の DoubleAnimation、行の出入りは移動+不透明度
- 編集モードのサンプル行 8 種 (entries 空のとき)、編集枠は角丸破線

## 7. 周辺機能

- **マウスハイライト**: 独立 topmost ウィンドウ、直径 `mouseHighlightSize`(既定56、30-120)+24。左=塗り 35%+リング / 右=二重リング。Up で 0.25s fadeOut
- **巨大カーソル**: Mac 版は重ね描き (システムカーソルを消せない)。Windows は `SetSystemCursor` で本物を差し替え可能 → 実装時に方式選択 (まず重ね描きで移植し、SetSystemCursor は実験オプション)
- **ホットエッジ**: 上端 10px=フェード凍結 / 下端 10px=一時非表示+入力処理停止。150ms ポーリング、両方オフならタイマー停止。**下端はタスクバーと競合** → 実機確認の上で位置・判定を再検討
- **ホットキー**: `RegisterHotKey`。既定は ⌥⌘K 相当 → **Alt+Win+K** (仮、実機で衝突確認)。録音 UI は修飾キー必須 (Shift 単独は拒否)
- **トレイ**: Shell_NotifyIcon 自前ラッパ (`ITrayIcon`)。TaskbarCreated で再登録。メニュー: キー表示 / 表示編集モード / 位置リセット / 設定 / 自動起動 / 終了。Mac の「Dock アイコン」「両方非表示の禁止」は対象外 (トレイ常時表示)
- **自動起動**: HKCU Run (`IStartupManager` 抽象、MSIX 時は StartupTask 実装に差し替え)
- **単一インスタンス**: 名前付き Mutex
- **ローカライズ**: Mac 版と同じ `L(ja, en)` ヘルパー。言語設定 system/ja/en

## 8. プライバシー設計 (Windows 固有)

macOS と違い低レベルフックは**パスワード欄でも入力が取れてしまう** (Secure Desktop を除く)。
- `showAllKeys` 既定オフを維持 (コンボ・特殊キーのみ表示)
- 初回起動時に説明ダイアログ (何を表示するか / 何も記録・送信しないこと)
- 一時停止ホットキーの案内、README への明記
- UIPI 注記: 昇格 (管理者) プロセスにフォーカスがある間はキーが取れない — 権限ガイドの代わりに FAQ へ

## 9. 設定スキーマ (settings.json v1)

Mac 版キーを踏襲: overlayVisible, displayScale(0.5-5), holdDuration(0-5), fadeDuration(0.1-4), maxRows(1-8), stackFromTop, showAllKeys, countRepeats, stepModifierRelease, holdJudgeDelay(0.2-2), topEdgeFreeze, dragToMove, typingAnimation, followCursorScreen, keyStyle(0/1/2), rowAlignment(0/1/2), textColorHex, textOutline, textOutlineColorHex, keyColorHex, backgroundOpacity, backgroundEnabled, customImagePath, osLabelStyle(既定=windows), jisABCLabels(→JIS 刻印表記の意味を再定義するか実装時判断), plusSeparator, distinguishCase, kanaDisplay, globeOnImeKeys(既定オフ), modifierPressOrder, arrowGrouping(0/1/2), showKeyClickCombo, mouseHighlight, mouseColorHex, mouseHighlightSize, showClickInKeyDisplay, bigCursor, bigCursorSize, bigCursorColorHex, language, hotCornerHide, launchAtLogin, hotKey { vk, modifiers } (Win32 値)
- 追加: `version: 1`, `displayProfiles: { "<stableId>": { frame, style, ... 10 項目 } }`
- 廃止: showMenuBarIcon/showDockIcon (トレイ常時表示), customImageBookmark, showOptionSymbols
- 書き込み: 500ms デバウンス + 一時ファイル→アトミック置換

## 9.5 既知の課題: かな・IME まわりの見直し (VM 検証で判明、要再設計)

Mac と Windows で日本語入力の切り替え方・キー割り当てが根本的に違うため、
「キー表記」のかな関連は Mac 版の直訳では機能しない。個別課題:

1. **kanaDisplay が発動しない**: `ImmGetOpenStatus` (WM_IME_CONTROL) による IME オン判定が
   VM 環境で機能しなかった。TSF ベースのアプリや新 MS-IME で IMM32 互換が効かない
   ケースの調査が必要。判定手段の再検討 (TSF API / 半角全角キーの状態追跡など)
2. **切替キーの表記設計**: Mac は 英数/かな キーでモード切替だが、Windows は
   半角/全角 トグルが主で、無変換/変換 の役割も IME 設定 (新旧 MS-IME、ATOK、
   Google 日本語入力) やユーザー設定で異なる。「英数/かな → 無変換/変換」という
   Mac 版由来のショートカット互換対応表が実キーの役割と一致しない場合があるため、
   IME 切替キーの表示は割り当てを考慮した再設計が必要
3. **かな入力ユーザーの実地検証**: JIS かな配列表 (JisKanaTable) は机上移植のため、
   かな入力の実利用での確認が必要 (checklist D 項)

## 10. 既定値の非互換メモ (Mac 版との意図的差分)

| 項目 | Mac | Windows | 理由 |
|---|---|---|---|
| osLabelStyle 既定 | mac | windows | 表記機能の意味が反転 |
| globeOnImeKeys 既定 | on | off | 🌐 キーが存在しない |
| showOptionSymbols | あり | なし | ⌥ 記号は Mac 固有文化 |
| fn 単独表示・遅延 | あり | なし | fn が OS に届かない |
| メディアキー表示 | F 番号 | Windows 流ラベル | NX 写像が不要 |
| Dock/メニューバー表示切替 | あり | なし | トレイ常時表示 |
| 権限ガイド | あり | なし→プライバシーダイアログ | 権限モデルの差 |
