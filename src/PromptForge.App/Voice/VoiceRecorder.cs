using NAudio.Utils;
using NAudio.Wave;

namespace PromptForge.App.Voice;

/// <summary>
/// Records the default microphone as 16 kHz mono 16-bit PCM (what Whisper uses internally),
/// so a 2-minute clip is about 3.8 MB, well under the 25 MB upload limit.
/// </summary>
internal sealed class VoiceRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly MemoryStream _pcm = new();
    private WaveIn? _waveIn;
    private TaskCompletionSource? _stopped;

    public static readonly WaveFormat Format = new(16000, 16, 1);

    private const int DefaultDevice = -1;

    public bool IsRecording { get; private set; }

    /// <summary>The raw 16-bit PCM from the last recording, for checking it contains sound before transcribing.</summary>
    public byte[] LastPcm { get; private set; } = [];
    public TimeSpan Duration => TimeSpan.FromSeconds(_pcm.Length / (double)Format.AverageBytesPerSecond);

    public void Start()
    {
        if (WaveIn.DeviceCount == 0) throw new InvalidOperationException("No microphone was found.");
        // -1 is WAVE_MAPPER: Windows' default recording device, following it if the user changes it.
        // Device 0 is just the first microphone in the list, which isn't the default when there are several.
        _waveIn = new WaveIn { WaveFormat = Format, BufferMilliseconds = 100, DeviceNumber = DefaultDevice };
        _waveIn.DataAvailable += (_, e) =>
        {
            lock (_gate) _pcm.Write(e.Buffer, 0, e.BytesRecorded);
        };
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waveIn.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null) Log.Error("recording stopped with error", e.Exception);
            _stopped.TrySetResult();
        };
        _waveIn.StartRecording();
        IsRecording = true;
    }

    /// <summary>Stops recording and returns the audio as a WAV file.</summary>
    public async Task<byte[]> StopAsync()
    {
        if (!IsRecording || _waveIn is null) return [];
        IsRecording = false;
        _waveIn.StopRecording();
        await Task.WhenAny(_stopped!.Task, Task.Delay(1500));
        byte[] pcm;
        lock (_gate) pcm = _pcm.ToArray();
        LastPcm = pcm;
        return ToWav(pcm);
    }

    internal static byte[] ToWav(byte[] pcm)
    {
        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(output), Format))
        {
            writer.Write(pcm, 0, pcm.Length);
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        try { _waveIn?.Dispose(); }
        catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException) { Log.Warn($"recorder dispose: {ex.Message}"); }
        _pcm.Dispose();
    }
}
