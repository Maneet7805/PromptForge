using System.Diagnostics;
using PromptForge.App.Infrastructure;
using PromptForge.Core.Updates;

namespace PromptForge.App.Flows;

/// <summary>
/// Looks for a newer release once a day (if enabled) and on request from the tray menu.
/// Never installs on its own: the user clicks the notification, the verified exe is downloaded,
/// and that exe's own installer asks "Update … to …?" exactly like a manual download does.
/// </summary>
internal sealed class UpdateController : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TimerInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);

    private readonly AppServices _services;
    private readonly UpdateChecker _checker;
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _busy;

    public UpdateController(AppServices services)
    {
        _services = services;
        _checker = new UpdateChecker(services.Http);
        _timer.Interval = (int)FirstCheckDelay.TotalMilliseconds;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = (int)TimerInterval.TotalMilliseconds;
            await CheckAsync(manual: false);
        };
    }

    /// <summary>The newer release found by the last check, if any.</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="Available"/> changes.</summary>
    public event Action? AvailableChanged;

    private static string CurrentVersion => Application.ProductVersion;

    public void Start() => _timer.Start();

    public async Task CheckAsync(bool manual)
    {
        var settings = _services.Settings;
        if (_busy) return;
        if (!manual)
        {
            if (!settings.CheckForUpdates) return;
            if (settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < CheckEvery) return;
        }

        _busy = true;
        try
        {
            var release = await _checker.CheckAsync(CurrentVersion);
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _services.SaveSettings();
            Log.Info($"update check current={CurrentVersion} latest={release?.Tag ?? "none newer"} manual={manual}");

            Available = release;
            AvailableChanged?.Invoke();
            if (release is not null)
                _services.NotifyClickable($"PromptForge {release.Version} is available. Click here to update.", ToolTipIcon.Info, () => _ = InstallAsync());
            else if (manual)
                _services.Notify($"You're on the latest version ({CurrentVersion}).", ToolTipIcon.Info);
        }
        catch (UpdateException ex)
        {
            Log.Warn("update check failed: " + ex.Message);
            if (manual) _services.Notify(ex.Message, ToolTipIcon.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Downloads the available release, verifies it, and runs its installer.</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } release || _busy) return;
        _busy = true;
        try
        {
            _services.Notify($"Downloading PromptForge {release.Version}…", ToolTipIcon.Info);
            var dir = Path.Combine(Path.GetTempPath(), "PromptForge-update", release.Version.ToString());
            var exe = await _checker.DownloadVerifiedAsync(release, dir, CurrentVersion);
            Log.Info($"update downloaded and verified version={release.Version}");
            // The new exe isn't in the install folder, so it offers to update the installed copy,
            // stops this one, and starts itself, the same path as a manual download.
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
        }
        catch (UpdateException ex)
        {
            Log.Warn("update failed: " + ex.Message);
            _services.Notify(ex.Message, ToolTipIcon.Warning);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("couldn't start the update", ex);
            _services.Notify("Couldn't start the update: " + ex.Message, ToolTipIcon.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose() => _timer.Dispose();
}
