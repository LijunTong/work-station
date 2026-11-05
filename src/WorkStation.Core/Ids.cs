namespace WorkStation.Core;

/// <summary>
/// ID 生成。Electron 版用 crypto.randomUUID()（带连字符的 36 位），
/// 这里用 Guid 的 N 格式（32 位无连字符）——两者都满足「唯一且不含需转义的字符」，
/// 且 SQLite 主键是 TEXT，两者等价。
/// </summary>
public static class Ids
{
    public static string NewId() => Guid.NewGuid().ToString("N");
}
