using System.Windows.Threading;
using KeyDisp.Core.Scheduling;

namespace KeyDisp.App.Services;

/// <summary>
/// UI スレッドの DispatcherTimer による IDelayScheduler 実装。
/// 状態機械・表示モデルと同じスレッドで発火するためロックは不要 (Mac 版と同型)。
/// </summary>
public sealed class DispatcherDelayScheduler : IDelayScheduler
{
    private sealed class Registration : IDisposable
    {
        public DispatcherTimer? Timer;

        public void Dispose()
        {
            Timer?.Stop();
            Timer = null;
        }
    }

    public long NowMs => Environment.TickCount64;

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var registration = new Registration();
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = delay < TimeSpan.Zero ? TimeSpan.Zero : delay,
        };
        timer.Tick += (_, _) =>
        {
            registration.Dispose();
            action();
        };
        registration.Timer = timer;
        timer.Start();
        return registration;
    }
}
