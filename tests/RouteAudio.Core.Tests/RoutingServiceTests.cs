using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;
using RouteAudio.Core.Rules;
using RouteAudio.Core.Models;
using RouteAudio.Core.Routing;
using Xunit;

namespace RouteAudio.Core.Tests;

public sealed class RoutingServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RouteAudioTests", Guid.NewGuid().ToString("n"));

    private static readonly DeviceDescriptor DeviceA = new()
    {
        Id = "{a}",
        Name = "扬声器 A",
        IsDefaultConsole = true,
        IsDefaultMultimedia = true,
    };

    private static readonly DeviceDescriptor DeviceB = new()
    {
        Id = "{b}",
        Name = "音箱 B",
    };

    private static readonly AppKey GameApp = AppKey.Create(@"C:\apps\game.exe");

    public RoutingServiceTests() => Directory.CreateDirectory(_directory);

    private string ConfigPath => Path.Combine(_directory, "config.json");

    [Fact]
    public async Task RouteAsync_AppliesBackendAndMarksPendingSession()
    {
        var (service, backend, sessions) = CreateService();
        sessions.Sessions.Add(Session(DeviceA, 100));

        var status = await service.RouteAsync(GameApp, DeviceB.Id);

        var request = Assert.Single(backend.Applied);
        Assert.Equal(DeviceB.Id, request.DeviceId);
        Assert.Equal(RouteState.AppliedPendingSession, status.State);
    }

    [Fact]
    public async Task Reconcile_MarksActivePolicyWhenSessionMovesToTarget()
    {
        var (service, backend, sessions) = CreateService();
        sessions.Sessions.Add(Session(DeviceA, 100));
        await service.RouteAsync(GameApp, DeviceB.Id);

        sessions.Sessions.Clear();
        sessions.Sessions.Add(Session(DeviceB, 100));
        await service.ReconcileAsync();

        Assert.Equal(RouteState.ActivePolicy, service.GetStatus(GameApp).State);
        Assert.Equal(2, backend.Applied.Count);
    }

    [Fact]
    public async Task RouteAsync_WhenAppNotRunning_IsPending()
    {
        var (service, _, sessions) = CreateService();
        sessions.Sessions.Clear();

        var status = await service.RouteAsync(GameApp, DeviceB.Id);

        Assert.Equal(RouteState.Pending, status.State);
        Assert.Equal(RouteReason.NotRunning, status.Reason);
    }

    [Fact]
    public async Task RouteAsync_WhenTargetMissing_DegradesToDefault()
    {
        var (service, backend, sessions) = CreateService();
        sessions.Sessions.Add(Session(DeviceA, 100));

        var status = await service.RouteAsync(GameApp, "{missing}");

        Assert.Equal(DeviceA.Id, Assert.Single(backend.Applied).DeviceId);
        Assert.Equal(RouteState.Degraded, status.State);
    }

    [Fact]
    public async Task Unroute_RemovesEntryAndClearsBackend()
    {
        var (service, backend, sessions) = CreateService();
        sessions.Sessions.Add(Session(DeviceA, 100));
        await service.RouteAsync(GameApp, DeviceB.Id);

        await service.UnrouteAsync(GameApp);

        Assert.Empty(service.Config.ActiveProfile.Entries);
        Assert.Equal(1, backend.Removed);
        Assert.Equal(RouteState.NotConfigured, service.GetStatus(GameApp).State);
    }

    [Fact]
    public async Task Rules_AreAppliedToRunningAppsWithoutExplicitEntry()
    {
        var (service, backend, sessions) = CreateService();
        service.ActiveProfile.Rules.Add(new Rule
        {
            Name = "游戏规则",
            Priority = 10,
            Match = new ProcessNameGlobNode { Pattern = "game" },
            Actions = [new RouteToDeviceAction { DeviceId = DeviceB.Id }],
        });
        sessions.Sessions.Add(Session(DeviceA, 100));

        await service.ReconcileAsync();

        Assert.Contains(backend.Applied, r => r.DeviceId == DeviceB.Id);
    }

    [Fact]
    public async Task SetDeviceVolume_ForwardsToDeviceCatalog()
    {
        var devices = new FakeDeviceCatalog([DeviceA, DeviceB]);
        var sessions = new FakeSessionCatalog();
        var apps = new FakeAppCatalog { [100] = GameApp };
        var service = new RoutingService(devices, sessions, apps, new FakeBackend(), new ConfigStore(ConfigPath));
        using var _ = service;

        var ok = await service.SetDeviceVolumeAsync(DeviceB.Id, 0.42);

        Assert.True(ok);
        var call = Assert.Single(devices.VolumeCalls);
        Assert.Equal(DeviceB.Id, call.DeviceId);
        Assert.Equal(0.42, call.Volume, 3);
    }

    [Fact]
    public async Task SetDeviceMute_ForwardsToDeviceCatalog()
    {
        var devices = new FakeDeviceCatalog([DeviceA, DeviceB]);
        var sessions = new FakeSessionCatalog();
        var apps = new FakeAppCatalog();
        var service = new RoutingService(devices, sessions, apps, new FakeBackend(), new ConfigStore(ConfigPath));
        using var _ = service;

        var ok = await service.SetDeviceMuteAsync(DeviceA.Id, true);

        Assert.True(ok);
        var call = Assert.Single(devices.MuteCalls);
        Assert.Equal(DeviceA.Id, call.DeviceId);
        Assert.True(call.Muted);
    }

    [Fact]
    public async Task BackendUnavailable_MarksFailure()
    {
        var (service, backend, sessions) = CreateService();
        backend.Available = false;
        sessions.Sessions.Add(Session(DeviceA, 100));
        service.ActiveProfile.Entries.Add(new RouteEntry
        {
            App = GameApp,
            Target = RouteTarget.ToDevice(DeviceB.Id),
        });

        await service.ReconcileAsync();

        Assert.Equal(RouteState.Failed, service.GetStatus(GameApp).State);
        Assert.Equal(RouteReason.PolicyApiUnavailable, service.GetStatus(GameApp).Reason);
    }

    private (RoutingService Service, FakeBackend Backend, FakeSessionCatalog Sessions) CreateService()
    {
        var devices = new FakeDeviceCatalog([DeviceA, DeviceB]);
        var sessions = new FakeSessionCatalog();
        var apps = new FakeAppCatalog { [100] = GameApp };
        var backend = new FakeBackend();
        var store = new ConfigStore(ConfigPath);
        var service = new RoutingService(devices, sessions, apps, backend, store);
        return (service, backend, sessions);
    }

    private static SessionInfo Session(DeviceDescriptor device, uint pid) => new()
    {
        EndpointId = device.Id,
        EndpointName = device.Name,
        ProcessId = pid,
        ProcessName = "game",
        ProcessPath = @"C:\apps\game.exe",
        IsActive = true,
        Volume = 1f,
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 忽略
        }
    }

    private sealed class FakeDeviceCatalog(IReadOnlyList<DeviceDescriptor> devices) : IDeviceCatalog
    {
        public List<(string DeviceId, double Volume)> VolumeCalls { get; } = [];

        public List<(string DeviceId, bool Muted)> MuteCalls { get; } = [];

        public IReadOnlyList<DeviceDescriptor> GetDevices() => devices;

        public bool SetVolume(string deviceId, double volume)
        {
            VolumeCalls.Add((deviceId, volume));
            return true;
        }

        public bool SetMute(string deviceId, bool muted)
        {
            MuteCalls.Add((deviceId, muted));
            return true;
        }
    }

    private sealed class FakeSessionCatalog : ISessionCatalog
    {
        public List<SessionInfo> Sessions { get; } = [];

        public IReadOnlyList<SessionInfo> GetSessions() => Sessions.ToList();

        public int SetMute(uint processId, bool muted) => 0;

        public int SetVolume(uint processId, float level) => 0;
    }

    private sealed class FakeAppCatalog : IAppCatalog
    {
        private readonly Dictionary<uint, AppKey> _apps = [];

        public AppKey this[uint pid]
        {
            set => _apps[pid] = value;
        }

        public AppKey Resolve(uint processId) =>
            _apps.TryGetValue(processId, out var app) ? app : AppKey.Create($"C:\\apps\\pid{processId}.exe", $"pid{processId}");
    }

    private sealed class FakeBackend : IRoutingBackend
    {
        public bool Available { get; set; } = true;

        public List<RouteRequest> Applied { get; } = [];

        public int Removed { get; private set; }

        public string Id => "fake";

        public BackendKind Kind => BackendKind.Policy;

        public bool IsAvailable => Available;

        public string StatusText => Available ? "fake ready" : "fake unavailable";

        public Task<RouteResult> ApplyAsync(RouteRequest request, CancellationToken cancellationToken)
        {
            Applied.Add(request);
            return Task.FromResult(RouteResult.Success(BackendKind.Policy));
        }

        public Task<RouteResult> RemoveAsync(uint processId, AppKey app, CancellationToken cancellationToken)
        {
            Removed++;
            return Task.FromResult(RouteResult.Success(BackendKind.Policy));
        }
    }
}

