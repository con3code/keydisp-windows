using KeyDisp.Core.Input;
using static KeyDisp.App.Interop.NativeMethods;

namespace KeyDisp.App.Interop;

/// <summary>GetAsyncKeyState / GetKeyState による実状態の照会 (Core の reconcile 用)。</summary>
public sealed class InputStateProbe : IInputStateProbe
{
    public bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public ModifierKeys RealModifiers
    {
        get
        {
            var flags = ModifierKeys.None;
            if (IsKeyDown(Vk.Control)) flags |= ModifierKeys.Control;
            if (IsKeyDown(Vk.Menu)) flags |= ModifierKeys.Alt;
            if (IsKeyDown(Vk.Shift)) flags |= ModifierKeys.Shift;
            if (IsKeyDown(Vk.LWin) || IsKeyDown(Vk.RWin)) flags |= ModifierKeys.Win;
            return flags;
        }
    }

    public bool IsCapsLockOn => (GetKeyState(Vk.CapsLock) & 1) != 0;
}
