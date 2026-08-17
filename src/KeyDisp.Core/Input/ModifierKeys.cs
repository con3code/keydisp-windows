namespace KeyDisp.Core.Input;

/// <summary>
/// 修飾キーの集合 (Mac 版 CGEventFlags の relevantFlags に相当)。
/// Windows に fn は届かないため fn は存在しない。
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}
