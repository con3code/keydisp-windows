using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Interop;

/// <summary>トレイメニューの 1 項目。</summary>
public sealed record TrayMenuItem(string? Text, bool IsChecked = false, Action? OnClick = null)
{
    public static readonly TrayMenuItem Separator = new((string?)null);
    public bool IsSeparator => Text is null;
}

/// <summary>
/// Shell_NotifyIcon の自前ラッパ (NuGet 依存を持たない方針のため)。
/// explorer.exe の再起動 (TaskbarCreated) で自動的に再登録する。
/// メニューは開くたびに menuProvider から構築する (チェック状態を最新にするため)。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 1;
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private readonly Func<IReadOnlyList<TrayMenuItem>> _menuProvider;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _hIcon;
    private bool _added;

    public TrayIcon(MessageWindow window, string tooltip,
        Func<IReadOnlyList<TrayMenuItem>> menuProvider, IntPtr hIcon = default)
    {
        _window = window;
        _menuProvider = menuProvider;
        Tooltip = tooltip;
        _hIcon = hIcon;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        _window.AddHook(WndProc);
        Add();
    }

    public string Tooltip { get; }

    private NOTIFYICONDATAW MakeData() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = CallbackMessage,
        hIcon = _hIcon != IntPtr.Zero ? _hIcon : LoadIconW(IntPtr.Zero, IDI_APPLICATION),
        szTip = Tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void Add()
    {
        var data = MakeData();
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)CallbackMessage && wParam == (IntPtr)IconId)
        {
            var mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg is WM_RBUTTONUP or WM_LBUTTONUP)
            {
                ShowMenu();
                handled = true;
            }
        }
        else if (msg == (int)_taskbarCreatedMessage)
        {
            // explorer.exe が再起動した: アイコンを登録し直す
            Add();
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var items = _menuProvider();
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsSeparator)
                {
                    AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                }
                else
                {
                    var flags = MF_STRING;
                    if (item.IsChecked) flags |= MF_CHECKED;
                    if (item.OnClick is null) flags |= MF_GRAYED;
                    AppendMenuW(menu, flags, (UIntPtr)(i + 1), item.Text);
                }
            }
            // メニュー外クリックで閉じるための定石
            SetForegroundWindow(_window.Handle);
            GetCursorPos(out var pt);
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                pt.X, pt.Y, _window.Handle, IntPtr.Zero);
            if (cmd > 0 && cmd <= items.Count)
            {
                items[cmd - 1].OnClick?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = MakeData();
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
        _window.RemoveHook(WndProc);
    }
}
