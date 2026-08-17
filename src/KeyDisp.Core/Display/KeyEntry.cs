namespace KeyDisp.Core.Display;

/// <summary>行の表示段階。Mac 版 KeyDisplayModel.swift の KeyEntry.Phase に対応。</summary>
public enum KeyEntryPhase
{
    /// <summary>キーが押されている間</summary>
    Active,
    /// <summary>離した後、設定時間だけ表示を維持</summary>
    Holding,
    /// <summary>フェードアウト中</summary>
    Fading,
}

/// <summary>
/// 画面に表示する 1 行分のキー入力。
/// tokens 例: ["Ctrl", "Shift", "S"] / タイピングなら ["H", "E", "L"]。
/// 変更操作は KeyDisplayModel からのみ行う。
/// </summary>
public sealed class KeyEntry
{
    private readonly List<string> _tokens;

    internal KeyEntry(IEnumerable<string> tokens, bool isTyping, KeyEntryPhase phase)
    {
        Id = Guid.NewGuid();
        _tokens = new List<string>(tokens);
        IsTyping = isTyping;
        Phase = phase;
    }

    public Guid Id { get; }
    public IReadOnlyList<string> Tokens => _tokens;
    public bool IsTyping { get; internal set; }
    public KeyEntryPhase Phase { get; internal set; }

    /// <summary>同じキーが連続で押された回数。2 以上で ×n バッジを表示する。</summary>
    public int Count { get; internal set; } = 1;

    public string Text => string.Concat(_tokens);

    /// <summary>編集モードのプレビュー用サンプル行を作る (モデル管理外)。</summary>
    public static KeyEntry Sample(IEnumerable<string> tokens, bool isTyping, int count = 1) =>
        new(tokens, isTyping, KeyEntryPhase.Holding) { Count = count };

    internal void ReplaceTokens(IEnumerable<string> tokens)
    {
        _tokens.Clear();
        _tokens.AddRange(tokens);
    }

    internal void AppendToken(string token) => _tokens.Add(token);
}
