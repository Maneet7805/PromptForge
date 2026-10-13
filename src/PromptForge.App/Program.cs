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

        // A copy run from anywhere else (e.g. the dist folder) offers to install or update itself.
        if (!SelfInstaller.IsRunningFromInstallDir && !args.Contains("--portable") && SelfInstaller.OfferInstall())
            return 0;

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        if (!TryAcquire(mutex, TimeSpan.Zero))
        {
            // Another copy is running. If it's on its way out (an update just restarted us), wait for it;
            // otherwise ask it to show its settings window and quit.
            if (!TryAcquire(mutex, TimeSpan.FromSeconds(3)))
            {
                if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
                return 0;
            }
        }

        Application.ThreadException += (_, e) => Log.Error("unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("unhandled exception", e.ExceptionObject as Exception);

        var background = args.Contains("--background");
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, SelfInstaller.ExitEventName);
        var uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);
        var context = new TrayContext(background);

        var listener = new Thread(() =>
        {
            var handles = new WaitHandle[] { showSettings, exitRequested };
            while (true)
            {
                var signalled = WaitHandle.WaitAny(handles);
                if (signalled == 0)
                {
                    uiContext.Post(_ => context.OnSecondInstance(), null);
                }
                else
                {
                    uiContext.Post(_ => context.ExitThread(), null);
                    return;
                }
            }
        })
        { IsBackground = true, Name = "InstanceSignals" };
        listener.Start();

        Application.Run(context);
        mutex.ReleaseMutex();
        return 0;
    }

    private static bool TryAcquire(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true; // previous owner was killed; we own it now
        }
    }
}
