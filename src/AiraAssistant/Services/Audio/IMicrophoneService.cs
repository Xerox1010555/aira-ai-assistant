using System;
using System.Threading;
using System.Threading.Tasks;

namespace AiraAssistant.Services.Audio;

/// <summary>
/// One chunk of PCM samples coming out of the input device.
/// </summary>
public sealed class AudioSampledEventArgs : EventArgs
{
    public byte[] Samples { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public AudioSampledEventArgs(byte[] samples, int sampleRate, int channels)
    {
        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }
}

/// <summary>
/// Wraps the chosen audio capture API. We push raw PCM into the STT engine;
/// the engine deals with resampling, if needed.
/// </summary>
public interface IMicrophoneService
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    void Stop();

    event EventHandler<AudioSampledEventArgs>? AudioSampled;
    event EventHandler<Exception>? CaptureError;
}
