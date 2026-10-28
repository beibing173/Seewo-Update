using System.Threading;
using SeewoUpdate;

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
        Application.Run(new TrayApp());
    }
}
