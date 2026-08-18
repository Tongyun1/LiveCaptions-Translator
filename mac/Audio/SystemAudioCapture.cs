using System;

using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace LiveCaptionsTranslator.Mac.Audio;

/// <summary>
/// 从指定输入设备（如 BlackHole 虚拟声卡）采集系统音频，
/// 统一输出 16kHz 单声道 float PCM，供语音识别（Whisper）使用。
/// miniaudio 会自动完成从设备原生采样率/声道到目标格式的重采样。
/// </summary>
public sealed class SystemAudioCapture : IDisposable
{
    /// <summary>Whisper 要求的采样率。</summary>
    public const int SampleRate = 16000;

    private static readonly AudioFormat CaptureFormat = new()
    {
        SampleRate = SampleRate,
        Channels = 1,
        Format = SampleFormat.F32
    };

    private readonly MiniAudioEngine _engine;
    private AudioCaptureDevice? _device;

    /// <summary>采集到新一批 16kHz 单声道样本时触发。</summary>
    public event Action<float[]>? SamplesAvailable;

    public SystemAudioCapture()
    {
        _engine = new MiniAudioEngine();
    }

    /// <summary>枚举当前可用的采集设备（BlackHole 安装后会出现在此列表）。</summary>
    public DeviceInfo[] GetCaptureDevices()
    {
        _engine.UpdateAudioDevicesInfo();
        return _engine.CaptureDevices;
    }

    /// <summary>
    /// 解析要采集的系统音频设备：优先匹配名称包含关键字的虚拟声卡（默认 BlackHole），
    /// 否则回退到系统默认采集设备。
    /// </summary>
    public DeviceInfo? ResolveSystemAudioDevice(string? preferredNameContains = "BlackHole")
    {
        var devices = GetCaptureDevices();
        if (devices.Length == 0)
            return null;

        if (!string.IsNullOrEmpty(preferredNameContains))
        {
            foreach (var d in devices)
            {
                if (d.Name?.Contains(preferredNameContains, StringComparison.OrdinalIgnoreCase) == true)
                    return d;
            }
        }

        foreach (var d in devices)
        {
            if (d.IsDefault)
                return d;
        }
        return devices[0];
    }

    /// <summary>开始采集。传入 null 时自动解析系统音频设备。</summary>
    public void Start(DeviceInfo? device = null)
    {
        if (_device is not null)
            return;

        DeviceInfo? target = device ?? ResolveSystemAudioDevice();
        var config = new MiniAudioDeviceConfig();

        _device = _engine.InitializeCaptureDevice(target, CaptureFormat, config);
        _device.OnAudioProcessed += OnAudioProcessed;
        _device.Start();
    }

    private void OnAudioProcessed(Span<float> samples, Capability capability)
    {
        if (capability != Capability.Record || samples.IsEmpty)
            return;

        SamplesAvailable?.Invoke(samples.ToArray());
    }

    public void Stop()
    {
        if (_device is null)
            return;

        _device.OnAudioProcessed -= OnAudioProcessed;
        _device.Stop();
        _device.Dispose();
        _device = null;
    }

    public void Dispose()
    {
        Stop();
        _engine.Dispose();
    }
}
