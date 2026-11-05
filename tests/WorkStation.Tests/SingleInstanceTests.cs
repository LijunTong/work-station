using WorkStation.App.Platform;

namespace WorkStation.Tests;

/// <summary>
/// 单实例的判定与唤醒。真正的「两个进程」只能手工验，
/// 这里守住两件可自动化的：同名互斥量第二次必定失败、没有监听者时唤醒不抛。
/// </summary>
public class SingleInstanceTests
{
    [Fact]
    public void TryStart_SecondCallWithSameName_ReturnsFalse()
    {
        var name = $"unit-{Guid.NewGuid():N}";

        Assert.True(SingleInstance.TryStart(name));
        try
        {
            Assert.False(SingleInstance.TryStart(name));
        }
        finally
        {
            SingleInstance.Stop();
        }
    }

    [Fact]
    public void TryStart_AfterStop_CanStartAgain()
    {
        var name = $"unit-{Guid.NewGuid():N}";

        Assert.True(SingleInstance.TryStart(name));
        SingleInstance.Stop();

        Assert.True(SingleInstance.TryStart(name));
        SingleInstance.Stop();
    }

    [Fact]
    public void SignalExisting_WithoutListener_DoesNotThrow()
    {
        SingleInstance.SignalExisting($"nobody-{Guid.NewGuid():N}");
    }
}
