using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkStation.Core.Data.Repositories;

namespace WorkStation.Tests;

/// <summary>设置仓储：JSON 值往返、类型读取与删除。</summary>
public class SettingsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SettingsRepository _settings;

    public SettingsRepositoryTests()
    {
        _connection = TestDb.OpenMigrated();
        _settings = new SettingsRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        Assert.Null(_settings.Get("missing"));
        Assert.Equal("fallback", _settings.GetString("missing", "fallback"));
        Assert.True(_settings.GetBoolean("missing", true));
    }

    [Fact]
    public void Set_String_RoundTrips()
    {
        _settings.Set("theme", "dark");

        Assert.Equal("dark", _settings.GetString("theme", "system"));
    }

    [Fact]
    public void Set_Boolean_RoundTrips()
    {
        _settings.Set("close_to_tray", false);

        Assert.False(_settings.GetBoolean("close_to_tray", true));
    }

    [Fact]
    public void Set_Object_RoundTrips()
    {
        _settings.Set("daily_review", new { enabled = true, time = "09:00" });

        var value = _settings.Get("daily_review");
        Assert.NotNull(value);
        Assert.Equal(JsonValueKind.Object, value.Value.ValueKind);
        Assert.Equal("09:00", value.Value.GetProperty("time").GetString());
    }

    [Fact]
    public void Set_OverwritesExistingKey()
    {
        _settings.Set("theme", "light");
        _settings.Set("theme", "dark");

        Assert.Equal("dark", _settings.GetString("theme", "system"));
        Assert.Single(_settings.GetAll());
    }

    [Fact]
    public void Remove_ReturnsTrueThenFalse()
    {
        _settings.Set("tmp", 1);

        Assert.True(_settings.Remove("tmp"));
        Assert.False(_settings.Remove("tmp"));
        Assert.Null(_settings.Get("tmp"));
    }

    [Fact]
    public void GetAll_ReturnsEveryKey()
    {
        _settings.Set("a", 1);
        _settings.Set("b", "two");

        var all = _settings.GetAll();

        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey("a"));
        Assert.True(all.ContainsKey("b"));
    }
}
