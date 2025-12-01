namespace v2.Models;

/// <summary>
/// キーボード入力イベントを表すデータモデル
/// </summary>
public class KeyEvent
{
    /// <summary>キーが押下されたときの時刻（UTC）</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>検出されたキーアクション（"toggle", "quit", "restart", "status"）</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>デバッグ用の詳細情報</summary>
    public string? Details { get; set; }

    public override string ToString()
    {
        return $"[{Timestamp:HH:mm:ss.fff}] Action: {Action}" +
               (Details != null ? $" - {Details}" : "");
    }
}
