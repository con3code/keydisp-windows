using KeyDisp.Core.Scheduling;

namespace KeyDisp.Core.Tests.TestSupport;

/// <summary>
/// 時間を手動で進める決定的スケジューラ。Advance 中に予約された項目も、
/// 期限が進行先より前なら同じ Advance の中で発火する。
/// </summary>
public sealed class VirtualScheduler : IDelayScheduler
{
    private sealed class Item : IDisposable
    {
        public long DueMs;
        public long Seq;
        public Action? Action;
        public void Dispose() => Action = null; // キャンセル
    }

    private readonly List<Item> _items = new();
    private long _seq;

    public long NowMs { get; private set; }

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var item = new Item
        {
            DueMs = NowMs + (long)delay.TotalMilliseconds,
            Seq = _seq++,
            Action = action,
        };
        _items.Add(item);
        return item;
    }

    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));

    public void Advance(TimeSpan delta)
    {
        var target = NowMs + (long)delta.TotalMilliseconds;
        while (true)
        {
            var next = _items
                .Where(i => i.Action is not null && i.DueMs <= target)
                .OrderBy(i => i.DueMs).ThenBy(i => i.Seq)
                .FirstOrDefault();
            if (next is null) break;
            _items.Remove(next);
            NowMs = Math.Max(NowMs, next.DueMs);
            var action = next.Action;
            next.Action = null;
            action?.Invoke();
        }
        NowMs = target;
        _items.RemoveAll(i => i.Action is null);
    }
}
