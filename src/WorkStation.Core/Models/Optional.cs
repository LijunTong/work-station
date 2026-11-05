namespace WorkStation.Core.Models;

/// <summary>
/// 「没传」与「传了 null」必须能区分开。
///
/// Electron 版的 update 语义是 <c>patch.x === undefined ? existing.x : patch.x</c>：
/// 没传 = 保留原值，传 null = 清空该字段。C# 的可空引用类型默认就是 null，
/// 「没赋值」和「显式赋 null」分不开，所以更新入参统一用 Optional&lt;T&gt; 表达三态：
/// <list type="bullet">
///   <item>None —— 没传，保留原值</item>
///   <item>Some(v) —— 传了 v（含 Some((string?)null) 表示清空）</item>
/// </list>
/// </summary>
public readonly struct Optional<T>
{
    public bool HasValue { get; }

    public T Value { get; }

    private Optional(bool hasValue, T value)
    {
        HasValue = hasValue;
        Value = value;
    }

    public static Optional<T> Some(T value) => new(true, value);

    public static Optional<T> None => new(false, default!);

    /// <summary>取新值；没传时回落到 <paramref name="fallback"/>。</summary>
    public T Or(T fallback) => HasValue ? Value : fallback;

    public override string ToString() => HasValue ? $"Some({Value})" : "None";
}
