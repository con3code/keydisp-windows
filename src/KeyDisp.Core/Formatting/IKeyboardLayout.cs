using KeyDisp.Core.Input;

namespace KeyDisp.Core.Formatting;

/// <summary>
/// キーボードレイアウト依存の情報 (Mac 版の UCKeyTranslate / TIS 相当)。
/// App 層が ToUnicodeEx / IMM32 で実装し、テストはフェイクを使う。
/// </summary>
public interface IKeyboardLayout
{
    /// <summary>
    /// 文字キーの表示文字を現在のレイアウトで取得する (実入力どおりの大小)。
    /// 取れなければ null (呼び出し側がフォールバックする)。
    /// </summary>
    string? CharacterFor(int vk, bool shifted, bool capsLock);

    /// <summary>日本語入力モード (IME オン) か。</summary>
    bool IsJapaneseInputMode();
}
