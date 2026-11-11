using System.Diagnostics;
using PromptForge.App.Infrastructure;
using PromptForge.App.UI;
using PromptForge.Core.Updates;

namespace PromptForge.App.Flows;

/// <summary>
/// Looks for a newer release once a day (if enabled) and on request from the tray menu.
/// Never installs on its own: the user clicks "Update now" on the update card, the verified exe is downloaded
/// (with progress shown), and that exe's own installer asks "Update … to …?" exactly like a manual download does.
/// </summary>
internal sealed class UpdateController : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TimerInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    private static readonly TimeSpan ShowFullBarFor = TimeSpan.FromMilliseconds(700);
    private const string ReleasePagePrefix = "https://github.com/";

    private readonly AppServices _services;
    private readonly UpdateChecker _checker;
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _busy;
    private UpdateForm? _card;
    private CancellationTokenSource? _cancel;
    private Version? _dismissedVersion;

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
            {
                // "Later" is remembered for this run only; a manual check always shows the card again.
                if (manual || _dismissedVersion != release.Version) ShowOffer(release);
            }
            else if (manual)
            {
                _services.Notify($"You're on the latest version ({CurrentVersion}).", ToolTipIcon.Info);
            }
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

    private void ShowOffer(ReleaseInfo release)
    {
        var card = EnsureCard();
        card.ShowOffer(release.Version.ToString(), CurrentVersion.Split('+')[0]);
    }

    private UpdateForm EnsureCard()
    {
        if (_card is { IsDisposed: false }) return _card;
        var card = new UpdateForm();
        card.UpdateClicked += () => _ = InstallAsync();
        card.LaterClicked += () =>
        {
            _dismissedVersion = Available?.Version;
            CloseCard();
        };
        card.CancelClicked += () =>
        {
            if (_cancel is { } cancel) cancel.Cancel(); // InstallAsync closes the card when the download stops
            else CloseCard();
        };
        card.ReleasePageClicked += OpenReleasePage;
        _card = card;
        return card;
    }

    private void CloseCard()
    {
        var card = _card;
        _card = null;
        card?.Close();
        card?.Dispose();
    }

    private void OpenReleasePage()
    {
        var url = Available?.PageUrl;
        if (url is null || !url.StartsWith(ReleasePagePrefix, StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Downloads the available release, verifies it, and runs its installer.</summary>
    public async Task InstallAsync()
    {
        if (Available is not { } release) return;
        if (_busy)
        {
            _card?.Show(); // a download is already running: the card is its only window
            return;
        }

        _busy = true;
        var version = release.Version.ToString();
        var card = EnsureCard();
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        var stage = UpdateStage.Preparing;
        var clock = Stopwatch.StartNew();
        try
        {
            card.ShowWorking(version);
            var progress = new Progress<UpdateProgress>(p =>
            {
                stage = p.Stage;
                if (!card.IsDisposed) card.SetProgress(version, p);
            });
            Log.Info($"update download started version={version}");
            var dir = Path.Combine(Path.GetTempPath(), "PromptForge-update", version);
            var exe = await _checker.DownloadVerifiedAsync(release, dir, CurrentVersion, cancel.Token, progress);
            Log.Info($"update downloaded and verified version={version} ms={clock.ElapsedMilliseconds}");

            card.ShowStarting();
            await Task.Delay(ShowFullBarFor);
            // The new exe isn't in the install folder, so it updates the installed copy, stops this one,
            // and starts itself. The flag tells it the user already chose "Update now", so it doesn't ask again.
            Process.Start(new ProcessStartInfo(exe, PromptForge.Core.Install.InstallPaths.FromUpdaterFlag) { UseShellExecute = true, WorkingDirectory = dir });
            CloseCard();
        }
        catch (OperationCanceledException)
        {
            Log.Info($"update cancelled stage={stage} ms={clock.ElapsedMilliseconds}");
            CloseCard();
        }
        catch (UpdateException ex)
        {
            Log.Warn("update failed: " + ex.Message);
            Fail(card, ex.Message);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("couldn't start the update", ex);
            Fail(card, "Couldn't start the update: " + ex.Message);
        }
        finally
        {
            _cancel = null;
            _busy = false;
        }
    }

    private void Fail(UpdateForm card, string message)
    {
        var canOpenPage = Available?.PageUrl.StartsWith(ReleasePagePrefix, StringComparison.OrdinalIgnoreCase) == true;
        card.ShowFailed(message, canOpenPage);
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        CloseCard();
        _timer.Dispose();
    }
}
