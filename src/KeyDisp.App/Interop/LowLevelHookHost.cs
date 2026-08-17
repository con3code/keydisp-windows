using System.Threading;
using System.Threading.Channels;
using Microsoft.Win32;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Interop;

/// <summary>フックから届く正規化済み入力イベント。</summary>
public readonly record struct RawInputEvent(
    RawInputKind Kind,
    int Vk,          // キーボードのとき
    bool IsDown,     // キー/ボタン
    int Button,      // マウスのとき: 0=左 1=右 2=中
    int X, int Y);   // マウスのとき (物理 px)

public enum RawInputKind
{
    Key,
    MouseButton,
    MouseMove,
}

/// <summary>
/// WH_KEYBOARD_LL / WH_MOUSE_LL を専用スレッドで動かすホスト。
/// コールバックは構造体のコピーと Channel への TryWrite だけを行い即 return する
/// (処理が遅いと LowLevelHooksTimeout でフックが外されるため)。
/// セッションロック解除・スリープ復帰時は無条件で再インストールする。
/// </summary>
public sealed class LowLevelHookHost : IDisposable
{
    private readonly Channel<RawInputEvent> _channel = Channel.CreateUnbounded<RawInputEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    // コールバックデリゲートは GC に回収されないようフィールドで保持する
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private volatile bool _disposed;

    private const uint MsgReinstall = WM_APP + 10;
    private const uint MsgQuit = WM_APP + 11;

    /// <summary>マウス移動も流すか (巨大カーソル等が必要になったらオンにする)。既定オフ。</summary>
    public bool ForwardMouseMoves { get; set; }

    public ChannelReader<RawInputEvent> Events => _channel.Reader;

    public LowLevelHookHost()
    {
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "KeyDisp.Hook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // ロック解除・復帰でフックが無効になっていることがあるため無条件で張り直す
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock) Reinstall();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Reinstall();
    }

    /// <summary>フックを張り直す (冪等)。フックスレッドへ依頼を送る。</summary>
    public void Reinstall()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, MsgReinstall, IntPtr.Zero, IntPtr.Zero);
    }

    private void ThreadProc()
    {
        _threadId = GetCurrentThreadId();
        InstallHooks();
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == MsgReinstall)
            {
                UninstallHooks();
                InstallHooks();
                continue;
            }
            if (msg.message == MsgQuit) break;
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        UninstallHooks();
    }

    private void InstallHooks()
    {
        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, IntPtr.Zero, 0);
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
    }

    private void UninstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var isUp = msg is WM_KEYUP or WM_SYSKEYUP;
            if (isDown || isUp)
            {
                _channel.Writer.TryWrite(new RawInputEvent(
                    RawInputKind.Key, (int)info.vkCode, isDown, 0, 0, 0));
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            switch (msg)
            {
                case WM_LBUTTONDOWN: WriteButton(0, true, info); break;
                case WM_LBUTTONUP: WriteButton(0, false, info); break;
                case WM_RBUTTONDOWN: WriteButton(1, true, info); break;
                case WM_RBUTTONUP: WriteButton(1, false, info); break;
                case WM_MBUTTONDOWN: WriteButton(2, true, info); break;
                case WM_MBUTTONUP: WriteButton(2, false, info); break;
                case WM_MOUSEMOVE:
                    if (ForwardMouseMoves)
                    {
                        _channel.Writer.TryWrite(new RawInputEvent(
                            RawInputKind.MouseMove, 0, false, 0, info.pt.X, info.pt.Y));
                    }
                    break;
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void WriteButton(int button, bool isDown, in MSLLHOOKSTRUCT info)
    {
        _channel.Writer.TryWrite(new RawInputEvent(
            RawInputKind.MouseButton, 0, isDown, button, info.pt.X, info.pt.Y));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_threadId != 0) PostThreadMessageW(_threadId, MsgQuit, IntPtr.Zero, IntPtr.Zero);
        _channel.Writer.TryComplete();
    }
}
