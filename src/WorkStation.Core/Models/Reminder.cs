namespace WorkStation.Core.Models;

public sealed record Reminder
{
    public required string Id { get; init; }

    public ReminderKind Kind { get; init; }

    public string? TaskId { get; init; }

    public required string Title { get; init; }

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm。</summary>
    public required string RemindAt { get; init; }

    public RepeatRule RepeatRule { get; init; } = RepeatRule.None;

    /// <summary>system | inapp。</summary>
    public string Channel { get; init; } = "system";

    public ReminderStatus Status { get; init; } = ReminderStatus.Pending;

    public string? FiredAt { get; init; }

    public required string CreatedAt { get; init; }

    public required string UpdatedAt { get; init; }
}
