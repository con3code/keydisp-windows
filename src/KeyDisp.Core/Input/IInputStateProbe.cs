namespace KeyDisp.Core.Input;

/// <summary>
/// 実際の入力デバイス状態の照会 (Mac 版 CGEventSource.keyState / flagsState 相当)。
/// App 層は GetAsyncKeyState / GetKeyState で実装。イベント取りこぼし時の reconcile に使う。
/// </summary>
public interface IInputStateProbe
{
    /// <summary>vk がいま物理的に押されているか。</summary>
    bool IsKeyDown(int vk);

    /// <summary>いま実際に押されている修飾キーの集合。</summary>
    ModifierKeys RealModifiers { get; }

    /// <summary>Caps Lock がオンか (distinguishCase の表示用)。</summary>
    bool IsCapsLockOn { get; }
}
