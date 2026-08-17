using KeyDisp.Core.Display;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Input;
using KeyDisp.Core.Layout;
using KeyDisp.Core.Scheduling;
using KeyDisp.Core.Settings;

namespace KeyDisp.Core.StateMachine;

/// <summary>
/// 入力イベントを KeyDisplayModel の行操作へ変換する状態機械。
/// Mac 版 KeyCaptureController.swift の状態機械部分の忠実な移植。
/// フック(App 層)から正規化済みイベントを受け取り、すべて単一スレッドで動く。
///
/// Mac 版との意図的な差分 (docs/SPEC.md §2):
/// - fn/🌐 の遅延表示・暗黙 fn 除去は削除 (fn は OS に届かない)
/// - ⌥記号 (optionSymbol) 関連は削除 (Alt は文字を打たないため、
///   タイピング連結を保つ修飾キーは Shift のみ)
/// - リピートは呼び出し側 (フック) が pressedKeys から合成して isRepeat で渡す
/// - Caps Lock は flagsChanged ではなく通常の keydown として届くので入口で分岐
/// </summary>
public sealed class KeyStateMachine
{
    private readonly KeyDisplayModel _model;
    private readonly AppSettings _settings;
    private readonly KeyFormatter _formatter;
    private readonly IDelayScheduler _scheduler;
    private readonly IInputStateProbe _probe;
    private readonly ITypingLayout _typingLayout;

    // ── 状態 ─────────────────────────────────────────────

    /// <summary>物理的に押下中の非修飾キー (修飾キーは _heldModifierVks で別管理)。</summary>
    private readonly HashSet<int> _pressedKeys = new();
    /// <summary>物理的に押下中の修飾キー VK (L/R 区別のまま)。</summary>
    private readonly HashSet<int> _heldModifierVks = new();

    private Guid? _currentId;
    private bool _currentIsModifierOnly;
    private ModifierKeys _currentModifiers = ModifierKeys.None;

    /// <summary>
    /// 修飾キー単独行に表示する内容。一連の操作で押された修飾キーの最大集合を保つ。
    /// (Ctrl+Shift を押して片方を先に離しても、表示は「Ctrl Shift」のまま残す)
    /// </summary>
    private ModifierKeys _modifierPeak = ModifierKeys.None;

    /// <summary>修飾キーが減ったときの処理を、押し続けるか確かめるまで保留しておくもの。</summary>
    private IDisposable? _modifierShrinkWork;

    /// <summary>修飾キーを押した順 (押下順表示オプション用)。</summary>
    private readonly List<ModifierKeys> _modifierPressOrder = new();

    /// <summary>修飾キー + クリックの表示行。</summary>
    private Guid? _mouseEntryId;

    /// <summary>直前のコンビネーション行 (同じキーの連続押しを ×n にまとめる判定用)。</summary>
    private Guid? _lastComboId;
    private List<string>? _lastComboTokens;

    /// <summary>直前の矢印キー行 (連続入力をまとめる判定用)。</summary>
    private Guid? _lastArrowId;
    private long _lastArrowTimeMs;

    /// <summary>タイピング (修飾なし文字入力) の連結用。</summary>
    private Guid? _lastTypingId;
    private long _lastTypingTimeMs;

    private const long TypingAppendWindowMs = 1200;
    /// <summary>連結の上限 (暴走を防ぐための安全弁)。</summary>
    private const int MaxTypingTokens = 400;

    public KeyStateMachine(
        KeyDisplayModel model, AppSettings settings, KeyFormatter formatter,
        IDelayScheduler scheduler, IInputStateProbe probe, ITypingLayout typingLayout)
    {
        _model = model;
        _settings = settings;
        _formatter = formatter;
        _scheduler = scheduler;
        _probe = probe;
        _typingLayout = typingLayout;
    }

    // ── 入口 ─────────────────────────────────────────────

    /// <summary>
    /// キーイベントの入口。フックから正規化済みの (vk, isDown, isRepeat) を受け取る。
    /// isRepeat はフック側で「既に押下中の vk の keydown」から合成する。
    /// </summary>
    public void HandleKey(int vk, bool isDown, bool isRepeat = false)
    {
        // 修飾キーは押下集合の更新だけ先に行う (物理状態の追跡は表示のオン/オフと無関係)
        var mod = KeyFormatter.ModifierOf(vk);
        if (mod != ModifierKeys.None)
        {
            if (isDown)
            {
                // Windows では押しっぱなしの修飾キーも keydown がリピートするので無視する
                if (isRepeat || !_heldModifierVks.Add(vk)) return;
            }
            else
            {
                _heldModifierVks.Remove(vk);
            }
            if (!GateVisible()) return;
            HandleFlagsChanged(ComputeFlags());
            ReleaseOrphanRows();
            return;
        }

        // Caps Lock はトグルなので一瞬だけ表示 (連打は ×n にまとめる)。keyup は無視
        if (vk == Vk.CapsLock)
        {
            if (!isDown || isRepeat) return;
            if (!GateVisible()) return;
            CancelModifierShrink();
            HandleCapsLockFlash();
            ReleaseOrphanRows();
            return;
        }

        if (!GateVisible()) return;
        if (isDown) HandleKeyDown(vk, isRepeat);
        else HandleKeyUp(vk);
        // 取り残された行があればここで回収する (複雑な組み合わせの取りこぼし対策)
        ReleaseOrphanRows();
    }

    /// <summary>マウスボタンの入口 (button: 0=左, 1=右, 2=中)。</summary>
    public void HandleMouseButton(int button, bool isDown)
    {
        HandleMouseForDisplay(button, isDown);
        ReleaseOrphanRows();
    }

    /// <summary>非表示中 (設定オフ or ホットエッジによる一時非表示) はキー入力を処理しない。</summary>
    private bool GateVisible()
    {
        if (_settings.OverlayVisible && !_settings.HotCornerSuppressed) return true;
        if (_pressedKeys.Count > 0 || _currentId is not null) Reset();
        return false;
    }

    /// <summary>
    /// 表示状態のリセット。物理押下の追跡 (_heldModifierVks) は現実を映しているので消さない
    /// (非表示中に修飾キーを押し、表示に戻ってから文字キーを押しても正しくコンボになるように)。
    /// </summary>
    public void Reset()
    {
        _pressedKeys.Clear();
        _currentId = null;
        _currentIsModifierOnly = false;
        _currentModifiers = ModifierKeys.None;
        _modifierPeak = ModifierKeys.None;
        _modifierPressOrder.Clear();
        CancelModifierShrink();
        _lastTypingId = null;
        _mouseEntryId = null;
        _lastComboId = null;
        _lastComboTokens = null;
        _lastArrowId = null;
    }

    private ModifierKeys ComputeFlags()
    {
        var flags = ModifierKeys.None;
        foreach (var vk in _heldModifierVks) flags |= KeyFormatter.ModifierOf(vk);
        return flags;
    }

    // ── keyDown ──────────────────────────────────────────

    private void HandleKeyDown(int vk, bool isRepeat)
    {
        // 長押しの autorepeat は、コンビネーション/特殊キー行のカウントとして数える。
        // ただし入力切替キーは何回リピートしてもモードが変わらないので数えない。
        if (isRepeat)
        {
            if (_settings.CountRepeats && _currentId is Guid rid && !_currentIsModifierOnly &&
                !KeyFormatter.NoRepeatVks.Contains(vk) &&
                _model.EntryOf(rid)?.IsTyping == false)
            {
                _model.Increment(rid);
            }
            return;
        }
        _pressedKeys.Add(vk);
        // 文字キーが押されたなら修飾キー行はコンビネーションへ変わるので、保留中の処理は破棄する
        CancelModifierShrink();

        var flags = ComputeFlags();
        _currentModifiers = flags;
        var shiftOnly = flags == ModifierKeys.Shift;
        var isChar = KeyFormatter.IsCharacterKey(vk);
        var now = _scheduler.NowMs;

        // 文章を打っている途中のスペースは、区切らずタイピングの一部として扱う。
        // 単独で押した場合 (連結が切れているとき) は従来どおり特殊キーとして表示する。
        var typingIsLive = _lastTypingId is Guid ltid0 &&
            now - _lastTypingTimeMs < TypingAppendWindowMs && _model.PhaseOf(ltid0) is not null;
        var spaceInTyping = vk == Vk.Space && (flags == ModifierKeys.None || shiftOnly) && typingIsLive;

        // 修飾キーなし、または Shift のみの文字キーは「タイピング」として扱う
        var isTypingKey = (flags == ModifierKeys.None || shiftOnly) && (isChar || spaceInTyping);

        if (isTypingKey)
        {
            // 連打カウント付きの修飾キー行 (Shift ×n など) があれば、この押下はタイピングの
            // 始まりだったので 1 回ぶん差し戻して履歴化する。タイピング表示の有無に
            // かかわらず先に処理する (「すべてのキー入力を表示」オフでも ×n を正しく保つ)
            if (_currentId is Guid mid0 && _currentIsModifierOnly && EntryCount(mid0) > 1)
            {
                _model.Decrement(mid0);
                _model.Release(mid0);
                _currentId = null;
                _currentIsModifierOnly = false;
            }
            // 通常タイピングは「すべてのキー入力を表示」がオンのときだけ表示する
            if (!_settings.ShowAllKeys) return;

            string token;
            if (_settings.KanaDisplay && _formatter.IsJapaneseInputMode() &&
                _formatter.KanaLabel(vk, shiftOnly) is string kana)
            {
                // かな入力モード: JIS かな配列のひらがな・記号で表示
                token = kana;
            }
            else
            {
                // 文章に続けるスペースは Windows 表記でも「␣」のまま。
                // 「Space」だと文章の途中に単語が混ざって読めなくなるため。
                token = _formatter.KeyLabel(
                    vk, shifted: shiftOnly,
                    capsLock: _probe.IsCapsLockOn,
                    applyLabelStyle: false,
                    preserveCase: _settings.DistinguishCase);
            }
            // 既存の修飾キー単独行 (Shift など) はタイピング行へ置き換える
            if (_currentId is Guid mid1 && _currentIsModifierOnly)
            {
                _model.Update(mid1, new[] { token }, isTyping: true);
                _currentIsModifierOnly = false;
                _lastTypingId = mid1;
                _lastTypingTimeMs = now;
                return;
            }
            // 直前のタイピング行へ連結
            if (_lastTypingId is Guid ltid &&
                now - _lastTypingTimeMs < TypingAppendWindowMs &&
                _model.PhaseOf(ltid) is not null &&
                TypingRowHasRoom(ltid, token) &&
                _model.Append(ltid, token))
            {
                _currentId = ltid;
                _lastTypingTimeMs = now;
                return;
            }
            // 新しいタイピング行。
            // 直前の行は連結を打ち切られてここへ来る (行数上限での分割や連結時間切れ)。
            // 押しっぱなし扱いのまま取り残されると消えなくなるので、必ず解放する。
            var id = _model.Begin(new[] { token }, isTyping: true);
            _model.ReleaseOtherTypingRows(exceptId: id);
            _currentId = id;
            _currentIsModifierOnly = false;
            _lastTypingId = id;
            _lastTypingTimeMs = now;
        }
        else if (HandleArrowGrouping(vk, flags, now))
        {
            // 矢印キーをまとめて表示した (→↓ の同時押しや連続操作)
        }
        else
        {
            // コンボ (修飾キー付き、または特殊キー単独)
            var tokens = _formatter.ModifierTokens(flags, _modifierPressOrder);
            tokens.Add(_formatter.KeyLabel(vk, shifted: false));
            _lastTypingId = null;
            _lastArrowId = null;
            _model.ReleaseOtherTypingRows();
            if (_currentId is Guid id && _currentIsModifierOnly)
            {
                if (MergeTargetId(tokens, ignoring: id) is Guid target)
                {
                    // 同じコンビネーションの連続押し: 「Ctrl」単独行を破棄して既存行を ×n に
                    _model.Remove(id);
                    _model.Increment(target);
                    _currentId = target;
                }
                else if (EntryCount(id) > 1)
                {
                    // 連打カウント付きの行 (Ctrl ×3 など) は履歴として残し、コンボは新しい行に。
                    // この押下で加算した 1 回ぶんはコンボの始まりだったので差し戻す (×4 → ×3)
                    _model.Decrement(id);
                    _model.Release(id);
                    _currentId = _model.Begin(tokens, isTyping: false);
                    _lastComboId = _currentId;
                    _lastComboTokens = tokens;
                }
                else
                {
                    // 「Ctrl」表示中に C が押された → 「Ctrl C」へ更新
                    _model.Update(id, tokens, isTyping: false);
                    _lastComboId = id;
                    _lastComboTokens = tokens;
                }
                _currentIsModifierOnly = false;
            }
            else
            {
                if (MergeTargetId(tokens) is Guid target)
                {
                    _model.Increment(target);
                    _currentId = target;
                }
                else
                {
                    if (_currentId is Guid old) _model.Release(old);
                    _currentId = _model.Begin(tokens, isTyping: false);
                    _lastComboId = _currentId;
                    _lastComboTokens = tokens;
                }
                _currentIsModifierOnly = false;
            }
        }
    }

    // ── keyUp ────────────────────────────────────────────

    private void HandleKeyUp(int vk)
    {
        _pressedKeys.Remove(vk);

        // 何かキーが残っているなら、押し続けた場合にその表示へ狭める
        if (_pressedKeys.Count > 0)
        {
            if (_currentId is not null) ArmRowNarrow();
            return;
        }
        // 物理キーが 1 つも押されていないなら、押しっぱなし扱いのタイピング行は残らないはず。
        // 早いタイピングでキーの押下が重なった際の取り残しをここで確実に回収する。
        _model.ReleaseOtherTypingRows(exceptId: _currentIsModifierOnly ? null : _currentId);
        if (_currentId is Guid id && !_currentIsModifierOnly)
        {
            if (_currentModifiers == ModifierKeys.None)
            {
                _model.Release(id);
                _currentId = null;
            }
            else
            {
                // 修飾キーはまだ押されている。押し続けるなら修飾キーだけの表示に狭める
                ArmRowNarrow();
            }
        }
    }

    // ── 修飾キー (flagsChanged 相当) ──────────────────────

    private void HandleFlagsChanged(ModifierKeys flags)
    {
        // 押した順を記録する (押下順表示オプション用)
        foreach (var (flag, _) in KeyFormatter.ModifierDisplayOrder)
        {
            if (flags.HasFlag(flag) && !_currentModifiers.HasFlag(flag))
            {
                _modifierPressOrder.Add(flag);
            }
        }
        _currentModifiers = flags;
        // 修飾キーの状態が動いたので、保留していた処理はいったん取り消す
        // (必要ならこの後 armRowNarrow で組み直す)
        CancelModifierShrink();

        if (flags == ModifierKeys.None)
        {
            _modifierPeak = ModifierKeys.None;
            _modifierPressOrder.Clear();
            if (_currentId is Guid id)
            {
                if (_pressedKeys.Count > 0)
                {
                    // 英数・かななど通常のキーがまだ押されている。
                    // 押し続けるならそのキーだけの表示に狭める
                    ArmRowNarrow();
                }
                else
                {
                    // 連打をまとめられるよう、離した行を記録しておく
                    _lastComboTokens = _model.EntryOf(id)?.Tokens.ToList();
                    _lastComboId = id;
                    _model.Release(id);
                    _currentId = null;
                    _currentIsModifierOnly = false;
                }
            }
        }
        else
        {
            if (_currentId is Guid id && _currentIsModifierOnly)
            {
                if (EntryCount(id) > 1)
                {
                    // 連打カウント付きの行 (Ctrl ×3 など) は残し、新しい修飾キー構成は新規行に。
                    // この押下で加算した 1 回ぶんは差し戻す
                    _model.Decrement(id);
                    _model.Release(id);
                    _modifierPeak = flags;
                    _currentId = _model.Begin(
                        _formatter.ModifierTokens(flags, _modifierPressOrder), isTyping: false);
                }
                else if ((_modifierPeak & ~flags) != ModifierKeys.None)
                {
                    // 修飾キーが減った。離しきる途中の一瞬で表示を変えないよう、
                    // 残りを押し続けたときだけ反映する (armRowNarrow を参照)
                    ArmRowNarrow();
                }
                else
                {
                    // 押し足した修飾キーは加えるが、離したぶんは消さない。
                    // 途中で片方を離しても「Ctrl Shift」のまま表示し続けるため。
                    _modifierPeak |= flags;
                    _model.Update(id,
                        _formatter.ModifierTokens(_modifierPeak, _modifierPressOrder), isTyping: false);
                }
            }
            else if (_currentId is null)
            {
                // タイピングの連結が生きている間の Shift 単独押下は、大文字や
                // 全角記号の入力操作の一部とみなして Shift 行を出さない。
                // これにより「きょう」や「Hello」が行分かれせず連続表示される。
                var now = _scheduler.NowMs;
                if (flags == ModifierKeys.Shift &&
                    _lastTypingId is Guid tid &&
                    now - _lastTypingTimeMs < TypingAppendWindowMs &&
                    _model.PhaseOf(tid) is not null)
                {
                    return;
                }
                var tokens = _formatter.ModifierTokens(flags, _modifierPressOrder);
                if (MergeTargetId(tokens) is Guid target)
                {
                    // 同じ修飾キーの連続押し (Ctrl 連打など): 既存行を ×n に
                    _model.Increment(target);
                    _modifierPeak = flags;
                    _currentId = target;
                }
                else
                {
                    // 修飾キー単独の表示を開始
                    _currentId = _model.Begin(tokens, isTyping: false);
                }
                _currentIsModifierOnly = true;
                // Shift は大文字を打つのに使うのでタイピングの連結を切らない。
                // (Mac 版は ⌥ も残すが、Windows の Alt は文字を打たないので切る)
                if ((flags & ~ModifierKeys.Shift) != ModifierKeys.None)
                {
                    _lastTypingId = null;
                }
            }
            else if (_currentId is Guid id2 && !_currentIsModifierOnly && EntryCount(id2) == 1)
            {
                // 通常のキーを押したままで修飾キーの構成が変わった。
                // 押し続けるなら、いま押しているキーだけの表示に整える
                ArmRowNarrow();
            }
        }
    }

    private void HandleCapsLockFlash()
    {
        var tokens = new List<string> { _formatter.Localized("⇪") };
        if (MergeTargetId(tokens) is Guid target)
        {
            _model.Increment(target);
            _model.Release(target);
        }
        else
        {
            var id = _model.Begin(tokens, isTyping: false);
            _model.Release(id);
            _lastComboId = id;
            _lastComboTokens = tokens;
        }
    }

    // ── マウス + キーの行 ─────────────────────────────────

    /// <summary>修飾キー + クリックをキー表示の行として出す。</summary>
    private void HandleMouseForDisplay(int button, bool isDown)
    {
        if (!_settings.ShowClickInKeyDisplay ||
            !_settings.OverlayVisible ||
            _settings.HotCornerSuppressed)
        {
            return;
        }

        if (isDown)
        {
            var flags = ComputeFlags();
            _currentModifiers = flags;
            // 押しっぱなしの文字キー (A を押しながらクリック、など)
            var heldChars = _settings.ShowKeyClickCombo
                ? _pressedKeys.Where(KeyFormatter.IsCharacterKey).OrderBy(k => k).ToList()
                : new List<int>();
            // 修飾キーか押しっぱなしの文字キーとの組み合わせのみ表示
            // (単独クリックはマウスハイライトが担当)
            if (flags == ModifierKeys.None && heldChars.Count == 0) return;
            var charLabels = heldChars.Select(k => _formatter.KeyLabel(k, shifted: false)).ToList();
            var tokens = _formatter.ModifierTokens(flags, _modifierPressOrder);
            tokens.AddRange(charLabels);
            tokens.Add(KeyFormatter.ClickToken(button));

            // 押している文字キーが既にタイピング行として出ているなら、
            // 新しい行を作らずその行を組み合わせ表示へ変える (「A」+「A🖱」の二重表示を防ぐ)
            if (charLabels.Count > 0 &&
                _currentId is Guid cid &&
                _model.PhaseOf(cid) == KeyEntryPhase.Active &&
                _model.EntryOf(cid)?.Tokens.SequenceEqual(charLabels) == true)
            {
                _model.Update(cid, tokens, isTyping: false);
                _mouseEntryId = cid;
                _currentId = null;
                _currentIsModifierOnly = false;
                _lastTypingId = null;
                _lastComboId = cid;
                _lastComboTokens = tokens;
                return;
            }
            _lastTypingId = null;
            if (_currentId is Guid id && _currentIsModifierOnly)
            {
                if (MergeTargetId(tokens, ignoring: id) is Guid target)
                {
                    // 同じ「修飾キー + クリック」の連続: 既存行を ×n に
                    _model.Remove(id);
                    _model.Increment(target);
                    _mouseEntryId = target;
                }
                else if (EntryCount(id) > 1)
                {
                    // 連打カウント付きの行は履歴として残し、クリック行は新規に。
                    // この押下で加算した 1 回ぶんは差し戻す
                    _model.Decrement(id);
                    _model.Release(id);
                    _mouseEntryId = _model.Begin(tokens, isTyping: false);
                    _lastComboId = _mouseEntryId;
                    _lastComboTokens = tokens;
                }
                else
                {
                    // 「Ctrl」表示中にクリック → 「Ctrl + クリック」へ転用
                    _model.Update(id, tokens, isTyping: false);
                    _mouseEntryId = id;
                    _lastComboId = id;
                    _lastComboTokens = tokens;
                }
                _currentIsModifierOnly = false;
                _currentId = null;
            }
            else
            {
                if (MergeTargetId(tokens) is Guid target)
                {
                    _model.Increment(target);
                    _mouseEntryId = target;
                }
                else
                {
                    if (_mouseEntryId is Guid old) _model.Release(old);
                    _mouseEntryId = _model.Begin(tokens, isTyping: false);
                    _lastComboId = _mouseEntryId;
                    _lastComboTokens = tokens;
                }
            }
        }
        else
        {
            if (_mouseEntryId is Guid mid)
            {
                _mouseEntryId = null;
                var held = ComputeFlags();
                _currentModifiers = held;
                if (held == ModifierKeys.None && _pressedKeys.Count == 0)
                {
                    _model.Release(mid);
                }
                else
                {
                    // 修飾キーを押し続けているなら、この行を押しているキーだけの表示に狭める
                    // (押しているのに何も表示されない状態を作らない)
                    _currentId = mid;
                    _currentIsModifierOnly = false;
                    ArmRowNarrow();
                }
            }
        }
    }

    // ── 矢印キーのまとめ ──────────────────────────────────

    /// <summary>
    /// 矢印キーを 1 行にまとめる。まとめた場合は true (呼び出し側は通常処理を行わない)。
    /// - 同時押しのみ: いま他の矢印キーも押されているときだけ 1 行にまとめる (→↓ の斜め移動)
    /// - 連続入力もまとめる: 続けて押した矢印も同じ行へ足していく (→→↓)
    /// </summary>
    private bool HandleArrowGrouping(int vk, ModifierKeys flags, long now)
    {
        if (_settings.ArrowGrouping == ArrowGrouping.Off ||
            !KeyFormatter.IsArrowKey(vk) ||
            // 修飾キーとの組み合わせは従来どおりコンビネーションとして扱う
            flags != ModifierKeys.None)
        {
            return false;
        }

        var token = _formatter.KeyLabel(vk, shifted: false);
        var otherArrowHeld = _pressedKeys.Any(k => k != vk && KeyFormatter.IsArrowKey(k));
        var withinWindow = now - _lastArrowTimeMs < TypingAppendWindowMs;
        var canJoin = _settings.ArrowGrouping switch
        {
            ArrowGrouping.Simultaneous => otherArrowHeld,
            ArrowGrouping.Consecutive => otherArrowHeld || withinWindow,
            _ => false,
        };

        if (canJoin && _lastArrowId is Guid aid && _model.PhaseOf(aid) is not null)
        {
            if (EntryCount(aid) > 1)
            {
                // ×n になった行へ別の矢印を足すと「→ ×4」が「→↓ ×4」に化けてしまう。
                // この押下で加算した 1 回ぶんを差し戻して履歴に残し、
                // いま押している組み合わせは新しい行にする (コンボと同じ扱い)
                _model.Decrement(aid);
                _model.Release(aid);
                var tokens = (_model.EntryOf(aid)?.Tokens ?? Array.Empty<string>()).ToList();
                tokens.Add(token);
                var newId = _model.Begin(tokens, isTyping: false);
                _currentId = newId;
                _currentIsModifierOnly = false;
                _lastArrowId = newId;
                _lastArrowTimeMs = now;
                _lastTypingId = null;
                _lastComboId = newId;
                _lastComboTokens = tokens;
                return true;
            }
            if (_model.Append(aid, token))
            {
                _currentId = aid;
                _currentIsModifierOnly = false;
                _lastArrowTimeMs = now;
                // 行の中身が変わったので、×n のまとめ先の照合にも増えた後の並びを使う
                _lastComboId = aid;
                _lastComboTokens = _model.EntryOf(aid)?.Tokens.ToList() ?? new List<string>();
                return true;
            }
        }

        // 同じ矢印だけを続けて押した場合 (同時押しでまとめられなかった場合) は、
        // 他のキーと同じく「同じキーの連続入力を ×n でまとめる」の設定に従う
        var single = new List<string> { token };
        if (MergeTargetId(single) is Guid mergeTarget)
        {
            if (_currentId is Guid cid && cid != mergeTarget) _model.Release(cid);
            _model.Increment(mergeTarget);
            _currentId = mergeTarget;
            _currentIsModifierOnly = false;
            _lastArrowId = mergeTarget;
            _lastArrowTimeMs = now;
            _lastTypingId = null;
            return true;
        }

        if (_currentId is Guid old) _model.Release(old);
        var id = _model.Begin(single, isTyping: false);
        _currentId = id;
        _currentIsModifierOnly = false;
        _lastArrowId = id;
        _lastArrowTimeMs = now;
        _lastTypingId = null;
        _lastComboId = id;
        _lastComboTokens = single;
        return true;
    }

    // ── 押しっぱなし判定 (行の狭め) ────────────────────────

    /// <summary>いま実際に押されているキーだけでトークン列を作る。</summary>
    private List<string> HeldTokens()
    {
        var tokens = _formatter.ModifierTokens(_currentModifiers, _modifierPressOrder);
        foreach (var vk in _pressedKeys.OrderBy(k => k))
        {
            tokens.Add(_formatter.KeyLabel(vk, shifted: false));
        }
        return tokens;
    }

    /// <summary>
    /// 押していたキーの一部を離した後、holdJudgeDelay だけ残りを押し続けたときの処理を予約する。
    /// - 「離すたびに履歴を残す」オン: そこまでの組み合わせを履歴として確定し、残りを新しい行にする
    /// - オフ: 行は増やさず、同じ行を残りのキーだけの表示へ狭める
    ///
    /// どちらも、それより早く離しきった場合は取り消され、押した組み合わせ全体が 1 行として残る。
    /// </summary>
    private void ArmRowNarrow()
    {
        CancelModifierShrink();
        _modifierShrinkWork = _scheduler.Schedule(
            TimeSpan.FromSeconds(_settings.HoldJudgeDelay), () =>
        {
            if (_currentId is not Guid id || EntryCount(id) != 1) return;
            var tokens = HeldTokens();
            if (tokens.Count == 0) return;
            if (_settings.StepModifierRelease)
            {
                _model.Release(id);
                _lastComboId = id;
                _lastComboTokens = _model.EntryOf(id)?.Tokens.ToList();
                _currentId = _model.Begin(tokens, isTyping: false);
            }
            else
            {
                _model.Update(id, tokens, isTyping: false);
            }
            _modifierPeak = _currentModifiers;
            // 通常のキーが残っていなければ、以後は修飾キー単独行として扱う
            _currentIsModifierOnly = _pressedKeys.Count == 0;
            _modifierShrinkWork = null;
        });
    }

    private void CancelModifierShrink()
    {
        _modifierShrinkWork?.Dispose();
        _modifierShrinkWork = null;
    }

    // ── 取り残された表示の回収 ─────────────────────────────

    /// <summary>
    /// コントローラが管理していない押しっぱなしの行を解放する。
    /// 何らかの理由で取り残された行は、ここで必ず通常のフェードに乗る。
    /// </summary>
    private void ReleaseOrphanRows()
    {
        foreach (var entry in _model.Entries.ToList())
        {
            if (entry.Phase == KeyEntryPhase.Active &&
                entry.Id != _currentId && entry.Id != _mouseEntryId)
            {
                _model.Release(entry.Id);
            }
        }
    }

    /// <summary>
    /// 実際のキーボードの状態と突き合わせて、表示のつじつまを合わせる。
    /// イベントを取りこぼしても (昇格アプリへのフォーカスや UAC 画面で keyup が届かない場合など)、
    /// ここで必ず現実に追いつく。1 秒周期の定期実行とイベント処理の両方から呼ばれる。
    /// </summary>
    public void ReconcileHeldState()
    {
        // 実際には離されているキーが押下中のまま残っていたら取り除く
        foreach (var vk in _pressedKeys.Where(k => !_probe.IsKeyDown(k)).ToList())
        {
            _pressedKeys.Remove(vk);
        }
        foreach (var vk in _heldModifierVks.Where(k => !_probe.IsKeyDown(k)).ToList())
        {
            _heldModifierVks.Remove(vk);
        }
        var realFlags = _probe.RealModifiers;
        if (_pressedKeys.Count == 0 && realFlags == ModifierKeys.None &&
            _mouseEntryId is null && _currentId is Guid id)
        {
            // 物理的には何も押していないのに押しっぱなし扱いの行が残っている
            _model.Release(id);
            _currentId = null;
            _currentIsModifierOnly = false;
            _modifierPeak = ModifierKeys.None;
        }
        ReleaseOrphanRows();
    }

    // ── 補助 ─────────────────────────────────────────────

    /// <summary>エントリの現在の連続カウント (存在しなければ 1)。</summary>
    private int EntryCount(Guid id) => _model.EntryOf(id)?.Count ?? 1;

    /// <summary>直前のコンビネーション行がまだ最後の行として生きていれば、その ID を返す (連続押しマージ用)。</summary>
    private Guid? MergeTargetId(IReadOnlyList<string> tokens, Guid? ignoring = null)
    {
        if (!_settings.CountRepeats) return null;
        if (_lastComboTokens is null || !_lastComboTokens.SequenceEqual(tokens)) return null;
        if (_lastComboId is not Guid lid || _model.PhaseOf(lid) is null) return null;
        // 間に別の行が挟まった場合はマージしない (連続した入力のみまとめる)
        var visibleLast = _model.Entries.LastOrDefault(e => e.Id != ignoring);
        if (visibleLast?.Id != lid) return null;
        return lid;
    }

    /// <summary>
    /// タイピング行にもう 1 文字入るか。連結上限 (400) と、タイプライター式スタイルでは
    /// 実測幅で判定する (Mac 版 typingRowHasRoom)。
    /// </summary>
    private bool TypingRowHasRoom(Guid id, string adding)
    {
        var tokens = _model.EntryOf(id)?.Tokens;
        if (tokens is null) return false;
        if (tokens.Count >= MaxTypingTokens) return false;
        if (!_typingLayout.UsesTypewriterWrap) return true;
        if (_settings.OverlayContentWidth <= 0) return true;
        var next = tokens.ToList();
        next.Add(adding);
        return _typingLayout.TypingLineFits(next);
    }
}
