using System.Text;
using KeyDisp.Core.Formatting;
using KeyDisp.Core.Input;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Interop;

/// <summary>
/// ToUnicodeEx によるレイアウト対応の文字取得と、IMM32 による日本語入力モード判定。
/// レイアウトは前面ウィンドウのスレッドのものを使う (ユーザーが見ている入力と一致させる)。
/// </summary>
public sealed class KeyboardLayoutProvider : IKeyboardLayout
{
    public string? CharacterFor(int vk, bool shifted, bool capsLock)
    {
        var layout = CurrentLayout();
        var scan = MapVirtualKeyW((uint)vk, MAPVK_VK_TO_VSC);
        if (scan == 0) return null;

        var state = new byte[256];
        if (shifted) state[Vk.Shift] = 0x80;
        if (capsLock) state[Vk.CapsLock] = 0x01;

        var buffer = new StringBuilder(8);
        // 0x4 = キーボード状態を変更しない (Win10 1809+)。デッドキー状態を壊さないために必須
        var rc = ToUnicodeEx((uint)vk, scan, state, buffer, buffer.Capacity,
            UNICODE_NO_KEYBOARD_STATE_CHANGE, layout);
        if (rc > 0) return buffer.ToString(0, rc);
        // rc == -1: デッドキー。バッファに入った文字 (´ など) をそのまま表示に使う
        if (rc == -1 && buffer.Length > 0) return buffer.ToString(0, 1);
        return null;
    }

    public bool IsJapaneseInputMode()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        var imeWnd = ImmGetDefaultIMEWnd(foreground);
        if (imeWnd == IntPtr.Zero) return false;
        // 別プロセスの IME ウィンドウへの問い合わせなのでタイムアウト付きで送る
        var ok = SendMessageTimeoutW(imeWnd, WM_IME_CONTROL,
            new IntPtr(IMC_GETOPENSTATUS), IntPtr.Zero,
            SMTO_ABORTIFHUNG, 50, out var result);
        return ok != IntPtr.Zero && result != IntPtr.Zero;
    }

    private static IntPtr CurrentLayout()
    {
        var foreground = GetForegroundWindow();
        var threadId = foreground == IntPtr.Zero
            ? 0u
            : GetWindowThreadProcessId(foreground, IntPtr.Zero);
        return GetKeyboardLayout(threadId);
    }
}
