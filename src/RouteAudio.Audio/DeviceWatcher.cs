using System.Runtime.InteropServices;
using RouteAudio.Interop;

namespace RouteAudio.Audio;

/// <summary>向系统注册设备事件（插入/移除/默认设备变化），用于快速触发对账。</summary>
public sealed class DeviceWatcher : IDisposable
{
    private readonly DeviceService _devices;
    private readonly NotificationClient _client;
    private readonly object _gate = new();
    private bool _registered;
    private DateTime _lastRaise = DateTime.MinValue;

    public DeviceWatcher(DeviceService devices)
    {
        _devices = devices;
        _client = new NotificationClient(OnRawNotification);
    }

    public event EventHandler? DevicesChanged;

    public bool IsActive => _registered;

    public (bool Ok, string? Error) Start()
    {
        try
        {
            _registered = _devices.RegisterNotificationClient(_client);
            return _registered ? (true, null) : (false, "RegisterEndpointNotificationCallback 返回失败");
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnRawNotification(string kind)
    {
        // 事件可能成串到达（蓝牙/USB 抖动），做 400ms 合并
        lock (_gate)
        {
            if (DateTime.UtcNow - _lastRaise < TimeSpan.FromMilliseconds(400)) return;
            _lastRaise = DateTime.UtcNow;
        }

        var handler = DevicesChanged;
        if (handler is null) return;

        try
        {
            handler(this, EventArgs.Empty);
        }
        catch
        {
            // 通知失败不影响音频服务
        }
    }

    public void Dispose()
    {
        if (!_registered) return;
        try
        {
            _devices.UnregisterNotificationClient(_client);
        }
        catch
        {
            // 忽略
        }

        _registered = false;
    }

    [ComVisible(true)]
    private sealed class NotificationClient(Action<string> onChanged) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            onChanged("state");
            return HResults.S_OK;
        }

        public int OnDeviceAdded(string deviceId)
        {
            onChanged("added");
            return HResults.S_OK;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            onChanged("removed");
            return HResults.S_OK;
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            onChanged("default");
            return HResults.S_OK;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            onChanged("property");
            return HResults.S_OK;
        }
    }
}
