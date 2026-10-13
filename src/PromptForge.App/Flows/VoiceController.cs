using PromptForge.App.Native;
using PromptForge.App.TextAccess;
using PromptForge.App.UI;
using PromptForge.App.Voice;
using PromptForge.Core.Llm;
using PromptForge.Core.Settings;

namespace PromptForge.App.Flows;

/// <summary>
/// One voice shortcut, two behaviours: tap it to start and tap again to stop, or hold it while
/// talking and release to finish. The transcript is typed at the cursor, or optionally sent to the
/// optimiser for review first. Dictation and optimisation stay separate unless the user opts in.
/// </summary>
internal sealed class VoiceController(AppServices services, OptimizeController optimizer)
{
    private static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(350);
    private static readonly Color Red = Color.FromArgb(225, 55, 55);
    private static readonly Color Blue = Color.FromArgb(80, 140, 255);

    private enum State { Idle, Recording, Transcribing }

    private State _state = State.Idle;
    private VoiceRecorder? _recorder;
    private StatusPill? _pill;
    private System.Windows.Forms.Timer? _poll;
    private DateTime _startedAt;
    private bool _toggleMode;
    private bool _cancelled;
    private IntPtr _target;
    private Keys _mainKey;
    private LlmEndpoint? _endpoint;

    public void OnHotkey(Keys mainKey)
    {
        switch (_state)
        {
            case State.Recording when _toggleMode:
                _ = StopAsync();
                return;
            case State.Recording or State.Transcribing:
                return;
        }

        if (!services.Settings.Voice.Enabled)
        {
            services.Notify("Voice input is turned off in Settings → Voice.", ToolTipIcon.Info);
            return;
        }
        if (optimizer.IsBusy) return;
        _endpoint = EndpointResolver.ResolveVoice(services.Settings, services.Secrets);
        if (_endpoint is null)
        {
            services.Notify("Add your Groq API key in Settings → Model to use voice input.", ToolTipIcon.Warning);
            return;
        }

        _target = Win32.GetForegroundWindow();
        _mainKey = mainKey;
        _cancelled = false;
        _toggleMode = false;
        _recorder = new VoiceRecorder();
        try
        {
            _recorder.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NAudio.MmException)
        {
            Log.Error("microphone start failed", ex);
            services.Notify("Couldn't start the microphone: " + ex.Message + " Check Windows Settings → Privacy → Microphone.", ToolTipIcon.Error);
            Cleanup();
            return;
        }

        _state = State.Recording;
        _startedAt = DateTime.UtcNow;
        _pill = new StatusPill();
        _pill.Clicked += Cancel;
        _pill.ShowMessage("Listening… release the keys to finish · click to cancel", Red);
        _poll = new System.Windows.Forms.Timer { Interval = 40 };
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        Log.Info("voice recording started");
    }

    private void Poll()
    {
        if (_state != State.Recording || _recorder is null) return;
        var elapsed = DateTime.UtcNow - _startedAt;
        if (elapsed.TotalSeconds >= Math.Clamp(services.Settings.Voice.MaxSeconds, 10, 600))
        {
            _ = StopAsync();
            return;
        }
        if (_toggleMode) return;

        if (!Win32.IsKeyDown((int)_mainKey))
        {
            if (elapsed < TapThreshold)
            {
                _toggleMode = true;
                var chord = services.Settings.Hotkeys.Voice;
                _pill?.ShowMessage($"Listening… press {chord} again to finish · click to cancel", Red);
            }
            else
            {
                _ = StopAsync();
            }
        }
    }

    private void Cancel()
    {
        if (_state != State.Recording) return;
        _cancelled = true;
        _ = StopAsync();
    }

    private async Task StopAsync()
    {
        if (_state != State.Recording || _recorder is null) return;
        _state = State.Transcribing;
        _poll?.Stop();
        try
        {
            var audio = await _recorder.StopAsync();
            var duration = _recorder.Duration;
            if (_cancelled)
            {
                Log.Info("voice cancelled");
                return;
            }
            if (duration < TimeSpan.FromMilliseconds(400))
            {
                services.Notify("That recording was too short.", ToolTipIcon.Info);
                return;
            }

            _pill?.ShowMessage("Transcribing…", Blue);
            var voice = services.Settings.Voice;
            var text = await services.Client.TranscribeAsync(_endpoint!, audio, "speech.wav",
                string.IsNullOrWhiteSpace(voice.Language) ? null : voice.Language, TimeSpan.FromSeconds(60), CancellationToken.None);
            Log.Info($"voice transcribed seconds={duration.TotalSeconds:0.0} chars={text.Length}");
            if (string.IsNullOrWhiteSpace(text))
            {
                services.Notify("Didn't catch any speech. Try again a little closer to the microphone.", ToolTipIcon.Info);
                return;
            }

            ClosePill();
            if (!await Keyboard.WaitForModifiersReleasedAsync(0, TimeSpan.FromSeconds(1)))
                Keyboard.ReleaseStuckModifiers();

            if (voice.AutoOptimize)
            {
                await optimizer.ReviewDictationAsync(text, _target);
                return;
            }

            var insertInto = Win32.GetForegroundWindow();
            var target = TargetInspector.Inspect(insertInto == IntPtr.Zero ? _target : insertInto);
            if (target.IsOwnWindow) target = TargetInspector.Inspect(_target);
            if (target.IsPasswordField)
            {
                ClipboardText.Write(text);
                services.Notify("That's a password field, so the text was copied instead of typed.", ToolTipIcon.Info);
                return;
            }
            var result = await TextBridge.InsertAsync(target.Hwnd, text, singleLine: target.IsTerminal);
            if (result.Outcome == ReplaceOutcome.CopiedInstead) services.Notify(result.Reason!, ToolTipIcon.Info);
        }
        catch (LlmException ex)
        {
            Log.Error("transcription failed", ex);
            services.Notify("Transcription failed: " + ex.UserMessage, ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("voice flow failed", ex);
            services.Notify("Voice input failed: " + ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            Cleanup();
        }
    }

    private void ClosePill()
    {
        _pill?.Close();
        _pill?.Dispose();
        _pill = null;
    }

    private void Cleanup()
    {
        _poll?.Stop();
        _poll?.Dispose();
        _poll = null;
        ClosePill();
        _recorder?.Dispose();
        _recorder = null;
        _state = State.Idle;
    }
}
