using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Interop;

/// <summary>
/// RegisterHotKey によるグローバルショートカット (Mac 版 HotKeyManager.swift 相当)。
/// 既定は Alt+Win+K (⌥⌘K のショートカット互換対応)。
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    private const int HotKeyId = 1;

    private readonly MessageWindow _window;
    private bool _registered;

    /// <summary>ホットキーが押された (UI スレッドで呼ばれる)。</summary>
    public event Action? Pressed;

    public HotKeyManager(MessageWindow window)
    {
        _window = window;
        _window.AddHook(WndProc);
    }

    /// <summary>登録し直す。既存の登録は破棄される。成功なら true (他アプリと衝突すると false)。</summary>
    public bool Register(int modifiers, int vk)
    {
        Unregister();
        _registered = RegisterHotKey(_window.Handle, HotKeyId, (uint)modifiers, (uint)vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotKeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == HotKeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(WndProc);
    }
}
