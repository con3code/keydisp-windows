namespace KeyDisp.Core.Scheduling;

/// <summary>
/// キャンセル可能な遅延実行の抽象 (Mac 版の DispatchWorkItem + asyncAfter に相当)。
/// 実装はすべて単一スレッド (UI スレッド) 上で action を呼ぶこと。
/// テストでは VirtualScheduler が時間を手動で進める。
/// </summary>
public interface IDelayScheduler
{
    /// <summary>delay 後に action を実行する予定を立てる。Dispose でキャンセル。</summary>
    IDisposable Schedule(TimeSpan delay, Action action);

    /// <summary>単調増加のミリ秒時刻 (タイピング連結窓などの経過判定用)。</summary>
    long NowMs { get; }
}
