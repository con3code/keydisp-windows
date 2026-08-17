using KeyDisp.Core.Input;

namespace KeyDisp.Core.Formatting;

/// <summary>
/// JIS かな配列 (VK → ひらがな・記号) の対応表。kanaDisplay オプション用。
/// Mac 版 KeyFormatter.swift の jisKana テーブルを Windows の VK/JIS 刻印で引き直したもの。
/// 記号キー (OEM_*) の割り当ては JIS 配列前提。実機での全キー確認は
/// DEVICE-TEST-CHECKLIST.md の D 項。
/// </summary>
internal static class JisKanaTable
{
    // (通常, Shift 時)。Shift 側が null のキーは通常文字を返す (Mac 版と同じ挙動)
    private static readonly Dictionary<int, (string Normal, string? Shifted)> Table = new()
    {
        // 数字段
        [0x31] = ("ぬ", null),          // 1
        [0x32] = ("ふ", null),          // 2
        [0x33] = ("あ", "ぁ"),          // 3
        [0x34] = ("う", "ぅ"),          // 4
        [0x35] = ("え", "ぇ"),          // 5
        [0x36] = ("お", "ぉ"),          // 6
        [0x37] = ("や", "ゃ"),          // 7
        [0x38] = ("ゆ", "ゅ"),          // 8
        [0x39] = ("よ", "ょ"),          // 9
        [0x30] = ("わ", "を"),          // 0
        [0xBD] = ("ほ", null),          // OEM_MINUS (-=)
        [0xDE] = ("へ", null),          // OEM_7 (^~) ※JIS
        [0xDC] = ("ー", null),          // OEM_5 (¥|)
        // 上段
        [0x51] = ("た", null),          // Q
        [0x57] = ("て", null),          // W
        [0x45] = ("い", "ぃ"),          // E
        [0x52] = ("す", null),          // R
        [0x54] = ("か", null),          // T
        [0x59] = ("ん", null),          // Y
        [0x55] = ("な", null),          // U
        [0x49] = ("に", null),          // I
        [0x4F] = ("ら", null),          // O
        [0x50] = ("せ", null),          // P
        [0xC0] = ("゛", null),          // OEM_3 (@`) ※JIS
        [0xDB] = ("゜", "「"),          // OEM_4 ([{)
        // 中段
        [0x41] = ("ち", null),          // A
        [0x53] = ("と", null),          // S
        [0x44] = ("し", null),          // D
        [0x46] = ("は", null),          // F
        [0x47] = ("き", null),          // G
        [0x48] = ("く", null),          // H
        [0x4A] = ("ま", null),          // J
        [0x4B] = ("の", null),          // K
        [0x4C] = ("り", null),          // L
        [0xBB] = ("れ", null),          // OEM_PLUS (;+) ※JIS
        [0xBA] = ("け", null),          // OEM_1 (:*) ※JIS
        [0xDD] = ("む", "」"),          // OEM_6 (]})
        // 下段
        [0x5A] = ("つ", "っ"),          // Z
        [0x58] = ("さ", null),          // X
        [0x43] = ("そ", null),          // C
        [0x56] = ("ひ", null),          // V
        [0x42] = ("こ", null),          // B
        [0x4E] = ("み", null),          // N
        [0x4D] = ("も", null),          // M
        [0xBC] = ("ね", "、"),          // OEM_COMMA (,<)
        [0xBE] = ("る", "。"),          // OEM_PERIOD (.>)
        [0xBF] = ("め", "・"),          // OEM_2 (/?)
        [0xE2] = ("ろ", null),          // OEM_102 (\_) ※JIS
    };

    public static string? Label(int vk, bool shifted)
    {
        if (!Table.TryGetValue(vk, out var pair)) return null;
        return shifted ? (pair.Shifted ?? pair.Normal) : pair.Normal;
    }
}
