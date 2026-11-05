using System.IO.Pipes;

namespace WorkStation.App.Platform;

/// <summary>
/// 单实例：第二次启动不该长出第二个窗口（两份进程会各弹一次提醒，还会抢同一个 SQLite 文件）。
///
/// 做法是「命名互斥量 + 命名管道」：
///   互斥量负责判定谁是第一个进程（跨平台，Unix 落在临时目录的锁文件上）；
///   管道负责把「有人又启动了」这件事告诉第一个进程，让它把窗口抬到前面。
/// 监听线程是后台线程，进程退出时自动结束，不需要显式 join。
/// </summary>
internal static class SingleInstance
{
    private const string Prefix = "WorkStation.App.";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _cancellation;

    /// <summary>已有实例被再次启动时触发。已在 UI 线程上投递，可以直接操作窗口。</summary>
    public static event Action? ActivationRequested;

    /// <summary>成为唯一实例并开始监听。返回 false 表示已有实例在跑，调用方应当退出。</summary>
    public static bool TryStart(string name)
    {
        var mutexName = Prefix + name;
        var pipeName = Prefix + name + ".pipe";

        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;

        // 先把 token 取出来再起线程：Stop() 可能在线程真正开跑之前就把 _cancellation 置空
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var token = cancellation.Token;

        var listener = new Thread(() => Listen(pipeName, token))
        {
            IsBackground = true,
            Name = "work-station-single-instance"
        };
        listener.Start();

        return true;
    }

    /// <summary>通知已经在跑的实例把窗口抬起来。找不到监听者就静默放弃。</summary>
    public static void SignalExisting(string name)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Prefix + name + ".pipe", PipeDirection.Out);
            client.Connect(1500);
            client.WriteByte(1);
            client.Flush();
        }
        catch (Exception)
        {
            // 第一个实例可能正在启动、还没开始监听。用户自己再点一次即可，不用报错
        }
    }

    private static void Listen(string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);

                server.WaitForConnection();
                server.ReadByte();

                Avalonia.Threading.Dispatcher.UIThread.Post(() => ActivationRequested?.Invoke());
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested) return;
                // 管道偶尔会被打断（句柄回收、权限），退一步再重建，不要因此退出监听
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>测试用：释放互斥量与监听线程。</summary>
    public static void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 当前线程不是持有者（测试里换过线程）：无所谓，进程退出时会一起释放
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
