using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PromptForge.Core.Voice;

/// <summary>
/// Silero VAD v5 (MIT, see THIRD-PARTY-NOTICES.md) run with ONNX Runtime. It judges whether audio is
/// speech, which loudness cannot: typing, clicks and breathing are loud enough but are not speech (#66).
/// </summary>
public sealed class SileroVad : ISpeechModel, IDisposable
{
    private const string ResourceName = "silero_vad.onnx";
    private const int SliceSamples = 512; // 32 ms at 16 kHz
    private const int ContextSamples = 64; // the model wants the end of the previous slice in front
    private const int StateSize = 2 * 1 * 128;

    private readonly InferenceSession _session;

    /// <summary>Throws if the embedded model or the native runtime can't be loaded; callers fall back to the loudness check.</summary>
    public SileroVad()
    {
        using var stream = typeof(SileroVad).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The speech model is missing from the build.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        _session = new InferenceSession(buffer.ToArray(), options);
    }

    public int SampleRate => 16000;

    public IReadOnlyList<float> Probabilities(ReadOnlySpan<byte> pcm16)
    {
        var samples = pcm16.Length / 2;
        var result = new List<float>(samples / SliceSamples);
        var state = new float[StateSize];
        var context = new float[ContextSamples];
        var input = new float[ContextSamples + SliceSamples];
        var rate = new DenseTensor<long>(new long[] { SampleRate }, [1]);

        for (var start = 0; start + SliceSamples <= samples; start += SliceSamples)
        {
            Array.Copy(context, input, ContextSamples);
            for (var i = 0; i < SliceSamples; i++)
            {
                var offset = (start + i) * 2;
                input[ContextSamples + i] = (short)(pcm16[offset] | (pcm16[offset + 1] << 8)) / 32768f;
            }
            Array.Copy(input, input.Length - ContextSamples, context, 0, ContextSamples);

            using var outputs = _session.Run(
            [
                NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, input.Length])),
                NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state, [2, 1, 128])),
                NamedOnnxValue.CreateFromTensor("sr", rate),
            ]);
            result.Add(outputs.First(o => o.Name == "output").AsTensor<float>().First());
            state = outputs.First(o => o.Name == "stateN").AsTensor<float>().ToArray();
        }
        return result;
    }

    public void Dispose() => _session.Dispose();
}
