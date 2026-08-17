using System.Windows.Interop;

namespace KeyDisp.App.Interop;

/// <summary>
/// トレイのコールバックとホットキーを受ける非表示のトップレベルウィンドウ。
/// TaskbarCreated のブロードキャストを受けるため、message-only window にはしない
/// (HWND_MESSAGE の子はブロードキャストが届かない)。
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public IntPtr Handle => _source.Handle;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("KeyDispMessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP (非表示のまま)
        };
        _source = new HwndSource(p);
    }

    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);
    public void RemoveHook(HwndSourceHook hook) => _source.RemoveHook(hook);

    public void Dispose() => _source.Dispose();
}
