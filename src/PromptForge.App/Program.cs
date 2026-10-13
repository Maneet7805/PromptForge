using PromptForge.App.Infrastructure;

namespace PromptForge.App;

internal static class Program
{
    private const string MutexName = @"Local\PromptForge.SingleInstance";
    private const string ShowSettingsEventName = @"Local\PromptForge.ShowSettings";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#pragma warning disable WFO5001 // Follow the Windows light/dark setting.
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        if (args.Length > 0 && args[0] == "--eval")
            return EvalCommand.Run(args[1..]);

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Already running: ask that instance to open its settings window, then quit.
            if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            return 0;
        }

        Application.ThreadException += (_, e) => Log.Error("unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("unhandled exception", e.ExceptionObject as Exception);

        var background = args.Contains("--background");
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        var uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);
        var context = new TrayContext(background);
        var listener = new Thread(() =>
        {
            while (showSettings.WaitOne())
                uiContext.Post(_ => context.OnSecondInstance(), null);
        })
        { IsBackground = true, Name = "SecondInstanceListener" };
        listener.Start();

        Application.Run(context);
        return 0;
    }
}
