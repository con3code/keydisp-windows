namespace KeyDisp.Core.Input;

/// <summary>本アプリで参照する Win32 仮想キーコード。</summary>
public static class Vk
{
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;        // Alt
    public const int Pause = 0x13;
    public const int CapsLock = 0x14;
    public const int Kana = 0x15;        // カタカナ/ひらがな
    public const int Kanji = 0x19;       // 半角/全角 (レイアウトにより 0xF3/0xF4 でも届く)
    public const int Escape = 0x1B;
    public const int Convert = 0x1C;     // 変換
    public const int NonConvert = 0x1D;  // 無変換
    public const int Space = 0x20;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int PrintScreen = 0x2C;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int Apps = 0x5D;        // メニューキー
    public const int F1 = 0x70;          // F1〜F24 = 0x70〜0x87
    public const int F24 = 0x87;
    public const int NumLock = 0x90;
    public const int ScrollLock = 0x91;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
    public const int VolumeMute = 0xAD;
    public const int VolumeDown = 0xAE;
    public const int VolumeUp = 0xAF;
    public const int MediaNext = 0xB0;
    public const int MediaPrev = 0xB1;
    public const int MediaStop = 0xB2;
    public const int MediaPlayPause = 0xB3;
    public const int OemAuto = 0xF3;     // 半角/全角 (IME オン時)
    public const int OemEnlw = 0xF4;     // 半角/全角 (IME オン時)

    public const int A = 0x41;
    public const int K = 0x4B;
    public const int Z = 0x5A;
    public const int D0 = 0x30;
    public const int D9 = 0x39;
    public const int Numpad0 = 0x60;
    public const int NumpadDivide = 0x6F;
}
