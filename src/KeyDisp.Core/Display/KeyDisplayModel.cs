using KeyDisp.Core.Scheduling;
using KeyDisp.Core.Settings;

namespace KeyDisp.Core.Display;

/// <summary>凍結の理由。複数が同時に成立しうる (上端フリーズ中にドラッグなど) ので集合で持つ。</summary>
public enum FreezeReason
{
    TopEdge,  // 画面上端にカーソルがある
    Dragging, // オーバーレイをドラッグ / リサイズ中
}

/// <summary>
/// 表示中のエントリ一覧と、そのライフサイクル (保持→フェード→削除) を管理する。
/// Mac 版 KeyDisplayModel.swift の忠実な移植。すべて単一スレッドから呼ぶこと。
/// </summary>
public sealed class KeyDisplayModel
{
    private readonly List<KeyEntry> _entries = new();
    private readonly AppSettings _settings;
    private readonly IDelayScheduler _scheduler;
    private readonly Dictionary<Guid, List<IDisposable>> _pendingWork = new();
    private readonly HashSet<FreezeReason> _freezeReasons = new();

    public KeyDisplayModel(AppSettings settings, IDelayScheduler scheduler)
    {
        _settings = settings;
        _scheduler = scheduler;
    }

    public IReadOnlyList<KeyEntry> Entries => _entries;

    /// <summary>エントリ一覧が変化した (UI はこれを購読して再描画する)。</summary>
    public event Action? Changed;

    private void NotifyChanged() => Changed?.Invoke();

    public Guid Begin(IEnumerable<string> tokens, bool isTyping)
    {
        var entry = new KeyEntry(tokens, isTyping, KeyEntryPhase.Active);
        _entries.Add(entry);
        TrimRows();
        NotifyChanged();
        return entry.Id;
    }

    public void Update(Guid id, IEnumerable<string> tokens, bool? isTyping = null)
    {
        var entry = Find(id);
        if (entry is null) return;
        entry.ReplaceTokens(tokens);
        // 内容が変わった行は別の入力になったということなので、連続カウントはリセット
        entry.Count = 1;
        if (isTyping is not null) entry.IsTyping = isTyping.Value;
        NotifyChanged();
    }

    /// <summary>タイピング中のエントリへ 1 文字追加。エントリが既に消えかけなら false。</summary>
    public bool Append(Guid id, string token)
    {
        var entry = Find(id);
        if (entry is null || entry.Phase == KeyEntryPhase.Fading) return false;
        CancelWork(id);
        entry.AppendToken(token);
        entry.Phase = KeyEntryPhase.Active;
        NotifyChanged();
        return true;
    }

    /// <summary>履歴を消さずに保持しているか (いずれかの理由が立っている間)。</summary>
    public bool IsFrozen => _freezeReasons.Count > 0;

    /// <summary>上端フリーズ用の従来 API (ホットエッジ監視から呼ばれる)。</summary>
    public void SetFrozen(bool frozen) => SetFreeze(FreezeReason.TopEdge, frozen);

    /// <summary>理由を指定して凍結を出し入れする。全体の凍結状態が変わったときだけ反映する。</summary>
    public void SetFreeze(FreezeReason reason, bool active)
    {
        var wasFrozen = IsFrozen;
        if (active) _freezeReasons.Add(reason);
        else _freezeReasons.Remove(reason);
        if (IsFrozen == wasFrozen) return;

        if (IsFrozen)
        {
            // 進行中の消去予定をすべて取り消し、消えかけの行は見える状態へ戻す
            foreach (var id in _pendingWork.Keys.ToList()) CancelWork(id);
            foreach (var entry in _entries)
            {
                if (entry.Phase == KeyEntryPhase.Fading) entry.Phase = KeyEntryPhase.Holding;
            }
        }
        else
        {
            // 解除時に、離されている行のフェードを改めて予約する
            foreach (var entry in _entries)
            {
                if (entry.Phase != KeyEntryPhase.Active) ScheduleFade(entry.Id);
            }
        }
        NotifyChanged();
    }

    /// <summary>キーを離した: 保持時間ののちフェードアウトさせる。</summary>
    public void Release(Guid id)
    {
        var entry = Find(id);
        if (entry is null || entry.Phase != KeyEntryPhase.Active) return;
        CancelWork(id);
        entry.Phase = KeyEntryPhase.Holding;
        ScheduleFade(id);
        NotifyChanged();
    }

    /// <summary>保持時間ののちフェードして消す予定を立てる。</summary>
    private void ScheduleFade(Guid id)
    {
        if (IsFrozen) return;
        CancelWork(id);

        var hold = Math.Max(0, _settings.HoldDuration);
        var fadeLen = Math.Max(0.05, _settings.FadeDuration);

        var fade = _scheduler.Schedule(TimeSpan.FromSeconds(hold), () =>
        {
            var entry = Find(id);
            if (entry is null) return;
            entry.Phase = KeyEntryPhase.Fading;
            NotifyChanged();
        });
        var remove = _scheduler.Schedule(TimeSpan.FromSeconds(hold + fadeLen + 0.1), () =>
        {
            _entries.RemoveAll(e => e.Id == id);
            _pendingWork.Remove(id);
            NotifyChanged();
        });
        _pendingWork[id] = new List<IDisposable> { fade, remove };
    }

    /// <summary>
    /// タイピング行は同時に 1 つだけ生きていればよい。行の分割や連結の打ち切りで
    /// 取り残された行 (押しっぱなし扱いのまま消えない行) を解放する。
    /// </summary>
    public void ReleaseOtherTypingRows(Guid? exceptId = null)
    {
        foreach (var entry in _entries.ToList())
        {
            if (entry.IsTyping && entry.Phase == KeyEntryPhase.Active && entry.Id != exceptId)
            {
                Release(entry.Id);
            }
        }
    }

    /// <summary>同じキーの連続入力: 回数を増やして表示を維持する。</summary>
    public void Increment(Guid id)
    {
        var entry = Find(id);
        if (entry is null) return;
        CancelWork(id);
        entry.Count += 1;
        entry.Phase = KeyEntryPhase.Active;
        NotifyChanged();
    }

    /// <summary>
    /// 連続カウントを 1 つ戻す (連打だと思ってマージした押下が、実はコンビネーションの
    /// 始まりだったと判明したときの差し戻し用)。
    /// </summary>
    public void Decrement(Guid id)
    {
        var entry = Find(id);
        if (entry is null) return;
        entry.Count = Math.Max(1, entry.Count - 1);
        NotifyChanged();
    }

    /// <summary>エントリを即座に削除する (回数マージで不要になった行の破棄用)。</summary>
    public void Remove(Guid id)
    {
        CancelWork(id);
        _entries.RemoveAll(e => e.Id == id);
        NotifyChanged();
    }

    /// <summary>Caps Lock 切替えなど、押しっぱなし判定のない一瞬の表示。</summary>
    public void Flash(IEnumerable<string> tokens)
    {
        var id = Begin(tokens, isTyping: false);
        Release(id);
    }

    public KeyEntryPhase? PhaseOf(Guid id) => Find(id)?.Phase;

    /// <summary>状態機械がトークン列・カウントを照会するためのアクセサ。</summary>
    public KeyEntry? EntryOf(Guid id) => Find(id);

    public void ClearAll()
    {
        foreach (var id in _pendingWork.Keys.ToList()) CancelWork(id);
        _entries.Clear();
        NotifyChanged();
    }

    private void TrimRows()
    {
        var maxRows = Math.Max(1, (int)_settings.MaxRows);
        while (_entries.Count > maxRows)
        {
            var removed = _entries[0];
            _entries.RemoveAt(0);
            CancelWork(removed.Id);
        }
    }

    private void CancelWork(Guid id)
    {
        if (_pendingWork.TryGetValue(id, out var items))
        {
            foreach (var item in items) item.Dispose();
            _pendingWork.Remove(id);
        }
    }

    private KeyEntry? Find(Guid id) => _entries.FirstOrDefault(e => e.Id == id);
}
