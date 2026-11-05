namespace WorkStation.Core.Models;

public sealed record Tag
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Color { get; init; }
}
