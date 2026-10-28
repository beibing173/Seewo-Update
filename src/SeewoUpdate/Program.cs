using System.Threading;
using SeewoUpdate;
using SeewoUpdate.Services;

internal static class Program
{
    private const string MutexName = @"Local\SeewoUpdate.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Log.Init();

        Application.ThreadException += (_, e) => Log.Error("界面线程未处理异常", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("未处理异常", e.ExceptionObject as Exception);

        Log.Info($"程序启动，参数：{string.Join(' ', args)}");
        Application.Run(new TrayApp(args));
        Log.Info("程序退出");
    }
}
