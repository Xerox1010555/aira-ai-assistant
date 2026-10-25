using System;
using System.Threading;
using System.Threading.Tasks;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AiraAssistant.Services.Audio;

/// <inheritdoc cref="IMicrophoneService" />
/// <summary>
/// v1.1: the capture format and optional input device now come from
/// <c>Audio.SampleRate / Audio.Channels / Audio.InputDeviceName</c> in
/// appsettings.json — the old code hard-coded 16 kHz/mono and ignored config.
/// Note: capture only starts when the active STT engine is push-based
/// (see <c>ISpeechToTextService.IsPushAudio</c>); System.Speech opens its
/// own device, so the old always-on NAudio capture was pure waste.
/// </summary>
public sealed class MicrophoneService : IMicrophoneService
{
    private readonly ILogger _logger;
    private readonly IAppConfig _config;
    private WaveInEvent? _waveIn;
    private readonly object _gate = new();
    private bool _isRunning;

    public event EventHandler<AudioSampledEventArgs>? AudioSampled;
    public event EventHandler<Exception>? CaptureError;

    public bool IsRunning => _isRunning;

    public MicrophoneService(ILogger logger, IAppConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_isRunning) return Task.CompletedTask;

            var sampleRate = _config.Audio.SampleRate is > 8000 and <= 48000 ? _config.Audio.SampleRate : 16000;
            var channels = _config.Audio.Channels is 1 or 2 ? _config.Audio.Channels : 1;

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(sampleRate, 16, channels),
                BufferMilliseconds = 100,
                DeviceNumber = ResolveDeviceNumber()
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) CaptureError?.Invoke(this, e.Exception);
            };
            try
            {
                _waveIn.StartRecording();
                _isRunning = true;
                _logger.Information($"Microphone started ({sampleRate} Hz / {channels} ch).");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to start microphone", ex);
                CaptureError?.Invoke(this, ex);
                throw;
            }
        }
        return Task.CompletedTask;
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_isRunning) return;
            try
            {
                _waveIn?.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to stop microphone", ex);
            }
            finally
            {
                _waveIn?.Dispose();
                _waveIn = null;
                _isRunning = false;
                _logger.Information("Microphone stopped.");
            }
        }
    }

    private int ResolveDeviceNumber()
    {
        var wanted = _config.Audio.InputDeviceName;
        if (string.IsNullOrWhiteSpace(wanted)) return -1; // default device

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            if (caps.ProductName.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Information($"Using input device #{i}: {caps.ProductName}");
                return i;
            }
        }

        _logger.Warning($"Input device '{wanted}' not found; falling back to default.");
        return -1;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        try
        {
            AudioSampled?.Invoke(this, new AudioSampledEventArgs(copy, _waveIn!.WaveFormat.SampleRate, _waveIn!.WaveFormat.Channels));
        }
        catch (Exception ex)
        {
            _logger.Error("AudioSampled subscriber threw", ex);
        }
    }
}
