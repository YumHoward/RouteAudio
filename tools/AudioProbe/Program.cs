using System.Diagnostics;
using RouteAudio.Audio;
using RouteAudio.Interop;

namespace RouteAudio.Probe;

/// <summary>
/// RouteAudio 技术验证工具（M0 / PoC）。
/// 用法：
///   AudioProbe devices
///   AudioProbe sessions
///   AudioProbe tone --seconds 60 [--freq 997]
///   AudioProbe policy [--pid N] [--device ID] [--role console|multimedia|communications|all] [--clear]
///   AudioProbe p1            # 策略路由端到端验证（核心 Gate）
/// </summary>
internal static class Program
{
    private const int ExitPass = 0;
    private const int ExitFail = 1;
    private const int ExitError = 2;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        using var com = ComApartment.InitializeMultithreaded();

        if (args.Length == 0) return Usage();

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "devices" => Devices(),
                "sessions" => Sessions(),
                "tone" => Tone(args),
                "policy" => Policy(args),
                "p1" => PolicyEndToEnd(),
                "p1b" => PolicyDiagnostics(),
                "p2" => LoopbackVsMute(),
                "c1" => VirtualCableBridge(),
                "p3" => StreamingLatency(args),
                "volume" => Volume(args),
                "help" or "-h" or "--help" => Usage(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return ExitError;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            RouteAudio AudioProbe（M0 技术验证工具）

              devices                          列出渲染端点
              sessions                         列出全部音频会话（跨端点）
              tone --seconds 60 [--freq 997]   播放测试音（当前进程即音源）
              policy [--pid N] [--device ID] [--role console|multimedia|communications|all] [--clear]
                                               读写「按进程默认端点」策略
              p1                               策略路由端到端验证（P1 Gate）
            """);
        return ExitPass;
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"未知命令: {cmd}");
        return Usage();
    }

    // ---------------- devices ----------------

    private static int Devices()
    {
        using var devices = new DeviceService();
        var list = devices.GetRenderEndpoints(DeviceState.ALL);
        Console.WriteLine($"渲染端点数量: {list.Count}");
        Console.WriteLine();

        foreach (var d in list)
        {
            var flags = string.Join(",", new[]
            {
                d.IsDefaultConsole ? "console" : null,
                d.IsDefaultMultimedia ? "multimedia" : null,
                d.IsDefaultCommunications ? "communications" : null,
            }.Where(x => x is not null));

            Console.WriteLine($"- {d.FriendlyName}");
            Console.WriteLine($"    id        : {d.Id}");
            Console.WriteLine($"    state     : {d.State}   enumerator: {d.EnumeratorName ?? "?"}   virtual: {d.LooksVirtual}");
            Console.WriteLine($"    interface : {d.InterfaceName ?? "?"}    desc: {d.Description ?? "?"}");
            Console.WriteLine($"    default   : {(flags.Length == 0 ? "-" : flags)}");
            var volume = devices.TryGetEndpointVolume(d.Id, out var muted, out var level, out var volumeHr);
            Console.WriteLine(volume
                ? $"    volume    : {level:P0}{(muted ? "  [已静音]" : "")}"
                : $"    volume    : 读取失败 0x{volumeHr:X8}");
        }

        return ExitPass;
    }

    // ---------------- sessions ----------------

    private static int Sessions()
    {
        using var devices = new DeviceService();
        var sessions = new SessionService(devices);
        var list = sessions.GetAllSessions();

        Console.WriteLine($"会话数量: {list.Count}");
        Console.WriteLine($"{"PID",-8}{"进程",-22}{"状态",-9}{"音量",-7}{"端点",-34}{"系统声"}");
        foreach (var s in list.OrderBy(s => s.EndpointName).ThenBy(s => s.ProcessName))
        {
            var endpoint = s.EndpointName.Length > 30 ? s.EndpointName[..30] : s.EndpointName;
            Console.WriteLine($"{s.ProcessId,-8}{Trim(s.ProcessName, 20),-22}{s.State,-9}{s.Volume,6:P0} {endpoint,-34}{(s.IsSystemSounds ? "是" : "")}");
        }

        return ExitPass;
    }

    // ---------------- tone ----------------

    private static int Tone(string[] args)
    {
        var seconds = GetIntArg(args, "--seconds", 60);
        var freq = GetDoubleArg(args, "--freq", 997);

        using var player = new TonePlayer();
        player.Start(frequency: freq);
        Console.WriteLine($"TONE pid={Environment.ProcessId} freq={freq}Hz seconds={seconds}");
        Console.Out.Flush();

        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        player.Stop();
        Console.WriteLine("TONE stopped");
        return ExitPass;
    }

    // ---------------- policy ----------------

    private static int Policy(string[] args)
    {
        var pid = (uint)GetIntArg(args, "--pid", Environment.ProcessId);
        var deviceId = GetStringArg(args, "--device", null);
        var roleArg = GetStringArg(args, "--role", "multimedia")!;
        var clear = args.Contains("--clear", StringComparer.OrdinalIgnoreCase);

        using var policy = AudioPolicyConfig.TryCreate(Console.WriteLine);
        if (policy is null)
        {
            Console.Error.WriteLine("[FAIL] AudioPolicyConfig 工厂不可用（无匹配 IID）");
            return ExitFail;
        }

        Console.WriteLine($"接口 IID: {policy.InterfaceId:D}");

        if (!policy.TryDetectSlots(pid, ERole.eMultimedia, Console.WriteLine))
        {
            Console.Error.WriteLine("[FAIL] 无法探测 Get/Set 虚表槽位");
            return ExitFail;
        }

        var roles = roleArg.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications }
            : new[] { ParseRole(roleArg) };

        foreach (var role in roles)
        {
            var hr = policy.Get(pid, EDataFlow.eRender, role, out var current);
            Console.WriteLine($"GET  pid={pid} role={role} -> hr=0x{hr:X8} device='{current}'");

            if (!clear && deviceId is not null)
            {
                hr = policy.Set(pid, EDataFlow.eRender, role, deviceId);
                Console.WriteLine($"SET  pid={pid} role={role} -> hr=0x{hr:X8} device='{deviceId}'");

                hr = policy.Get(pid, EDataFlow.eRender, role, out var after);
                Console.WriteLine($"GET  pid={pid} role={role} -> hr=0x{hr:X8} device='{after}'  {(after == deviceId ? "OK" : "MISMATCH")}");
            }
            else if (clear)
            {
                hr = policy.Set(pid, EDataFlow.eRender, role, null);
                Console.WriteLine($"CLEAR pid={pid} role={role} -> hr=0x{hr:X8}");
            }
        }

        return ExitPass;
    }

    // ---------------- C1：虚拟声卡桥接（B 路线失败后的替代） ----------------

    private static int VirtualCableBridge()
    {
        Console.WriteLine("=== C1 虚拟声卡桥接验证（策略 → 虚拟端点 → 设备回环采集）===");
        using var devices = new DeviceService();
        var sessions = new SessionService(devices);
        var endpoints = devices.GetRenderEndpoints();
        var outDir = Path.Combine(FindRepoRoot(), "artifacts", "poc");

        var physical = endpoints.FirstOrDefault(e => e.IsDefaultMultimedia) ?? endpoints[0];
        var virtualEndpoint = endpoints.FirstOrDefault(e => e.LooksVirtual && e.Id != physical.Id)
                              ?? endpoints.FirstOrDefault(e => e.Id != physical.Id);

        if (virtualEndpoint is null)
        {
            Console.Error.WriteLine("[FAIL] 没有可用的虚拟/第二端点");
            return ExitFail;
        }

        Console.WriteLine($"[物理端点] {physical.FriendlyName}");
        Console.WriteLine($"[虚拟端点] {virtualEndpoint.FriendlyName}  ({virtualEndpoint.Id})");

        using var policy = AudioPolicyConfig.TryCreate(Console.WriteLine);
        if (policy is null) return ExitFail;

        using var child1 = StartToneChild(90);
        var session1 = WaitForSession(sessions, (uint)child1.Id, TimeSpan.FromSeconds(10));
        if (session1 is null)
        {
            Console.Error.WriteLine("[FAIL] 测试音进程没有会话");
            Kill(child1);
            return ExitFail;
        }

        Console.WriteLine($"[初始] pid={child1.Id} 端点={session1.EndpointName}");
        if (!policy.TryDetectSlots((uint)child1.Id, ERole.eMultimedia, Console.WriteLine))
        {
            Console.Error.WriteLine("[FAIL] 槽位探测失败");
            Kill(child1);
            return ExitFail;
        }

        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia })
        {
            var hr = policy.Set((uint)child1.Id, EDataFlow.eRender, role, virtualEndpoint.Id);
            Console.WriteLine($"[写入] role={role} -> 0x{hr:X8}");
        }

        Kill(child1);
        Thread.Sleep(500);

        using var child2 = StartToneChild(90);
        var session2 = WaitForSession(sessions, (uint)child2.Id, TimeSpan.FromSeconds(10));
        if (session2 is null)
        {
            Console.Error.WriteLine("[FAIL] 第二个测试音进程没有会话");
            Kill(child2);
            return ExitFail;
        }

        Console.WriteLine($"[重启] pid={child2.Id} 端点={session2.EndpointName}");
        var routed = session2.EndpointId == virtualEndpoint.Id;
        Console.WriteLine(routed
            ? "[路由] 音频已进入虚拟端点"
            : $"[路由] 未进入虚拟端点（实际 {session2.EndpointName}）");

        // 设备回环采集虚拟端点
        using var capture = new DeviceLoopbackCapture(devices, virtualEndpoint.Id);
        var (ok, error) = capture.Start();
        if (!ok)
        {
            Console.Error.WriteLine($"[FAIL] 虚拟端点回环采集启动失败: {error}");
            Kill(child2);
            return ExitFail;
        }

        Thread.Sleep(1200);
        _ = capture.TakeStats();
        var stats = MeasureWindow(capture, outDir, "c1-cable-capture", 2500);

        // 检查是否还存在「直接送到物理端点」的同类会话（双声判断）
        var sessionsOnPhysical = sessions.GetSessionsForProcess((uint)child2.Id)
            .Where(s => s.EndpointId == physical.Id).ToList();

        Console.WriteLine();
        Console.WriteLine($"[双声检查] 测试音在物理端点上的会话数: {sessionsOnPhysical.Count}（应为 0 才能避免双声）");
        Console.WriteLine($"[采集结果] {stats}");

        var passed = routed && sessionsOnPhysical.Count == 0 && stats.RmsDb > -60;
        Console.WriteLine(passed
            ? "[PASS] C 路线可行：策略把应用送入虚拟端点，设备回环可正常采集，且无物理端点双声"
            : "[FAIL] C 路线未通过（见上方各项）");

        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia })
        {
            _ = policy.Set((uint)child2.Id, EDataFlow.eRender, role, null);
        }

        capture.Stop();
        Kill(child2);
        return passed ? ExitPass : ExitFail;
    }

    // ---------------- volume：设备音量读写（验证用） ----------------

    private static int Volume(string[] args)
    {
        using var devices = new DeviceService();
        var deviceId = GetStringArg(args, "--device", null);
        var level = GetDoubleArg(args, "--level", -1);
        var muteArg = GetStringArg(args, "--mute", null);

        var targets = deviceId is null
            ? devices.GetRenderEndpoints()
            : devices.GetRenderEndpoints().Where(d => d.Id == deviceId).ToList();

        foreach (var device in targets)
        {
            var before = devices.TryGetEndpointVolume(device.Id, out var mutedBefore, out var levelBefore, out var hr0);
            Console.WriteLine($"- {device.FriendlyName}");
            Console.WriteLine($"    id     : {device.Id}");
            Console.WriteLine(before
                ? $"    before : {levelBefore:P0}{(mutedBefore ? " [静音]" : "")}"
                : $"    before : 读取失败 0x{hr0:X8}");

            if (level >= 0 || muteArg is not null)
            {
                bool? mute = muteArg switch { "on" => true, "off" => false, _ => null };
                var ok = devices.TrySetEndpointVolume(device.Id, (float)level, mute, out var hr);
                Console.WriteLine(ok ? "    set    : OK" : $"    set    : 失败 0x{hr:X8}");

                if (devices.TryGetEndpointVolume(device.Id, out var mutedAfter, out var levelAfter, out _))
                {
                    Console.WriteLine($"    after  : {levelAfter:P0}{(mutedAfter ? " [静音]" : "")}");
                }
            }
        }

        return ExitPass;
    }

    // ---------------- P3：流式管线延迟与稳定性 ----------------

    private static int StreamingLatency(string[] args)
    {
        Console.WriteLine("=== P3 流式管线（进程回环 → 目标端点渲染）延迟与稳定性 ===");
        using var devices = new DeviceService();
        var sessions = new SessionService(devices);
        var endpoints = devices.GetRenderEndpoints();

        (bool Ok, bool Muted, float Volume) VolumeOf(DeviceEndpointInfo e) =>
            devices.TryGetEndpointVolume(e.Id, out var m, out var v, out _) ? (true, m, v) : (false, false, 1f);

        var requestedTarget = GetStringArg(args, "--target", null);
        DeviceEndpointInfo? target;
        if (requestedTarget is not null)
        {
            target = endpoints.FirstOrDefault(e => e.Id == requestedTarget);
        }
        else
        {
            target = endpoints
                .Where(e =>
                {
                    var vol = VolumeOf(e);
                    return vol.Ok && !vol.Muted && vol.Volume > 0.01f;
                })
                .OrderByDescending(e => !e.LooksVirtual)
                .FirstOrDefault();
            target ??= endpoints.FirstOrDefault(e => e.IsDefaultMultimedia) ?? endpoints[0];
        }

        if (target is null)
        {
            Console.Error.WriteLine("[FAIL] 没有可用的目标端点（可能全部静音或音量为 0）");
            return ExitFail;
        }

        var targetVolume = VolumeOf(target);
        Console.WriteLine($"[目标端点] {target.FriendlyName}（音量 {targetVolume.Volume:P0}{(targetVolume.Muted ? "，静音" : "")}）");

        // 找一个「静音 / 零音量」的端点作为原输出，避免桥接输出与原声叠加
        var silentSink = endpoints.FirstOrDefault(e =>
        {
            if (e.Id == target.Id) return false;
            var vol = VolumeOf(e);
            return vol.Ok && (vol.Muted || vol.Volume < 0.01f);
        });

        using var policy = AudioPolicyConfig.TryCreate(message => Console.WriteLine($"[policy] {message}"));

        var child = StartToneChild(150);
        var session = WaitForSession(sessions, (uint)child.Id, TimeSpan.FromSeconds(10));
        if (session is null)
        {
            Console.Error.WriteLine("[FAIL] 测试音进程没有创建会话");
            Kill(child);
            return ExitFail;
        }

        var redirected = false;
        if (policy is not null && silentSink is not null)
        {
            foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia })
            {
                _ = policy.Set((uint)child.Id, EDataFlow.eRender, role, silentSink.Id);
            }

            Kill(child);
            Thread.Sleep(500);
            child = StartToneChild(150);
            session = WaitForSession(sessions, (uint)child.Id, TimeSpan.FromSeconds(10));
            if (session is null)
            {
                Console.Error.WriteLine("[FAIL] 第二个测试音进程没有创建会话");
                Kill(child);
                return ExitFail;
            }

            redirected = session.EndpointId == silentSink.Id;
            Console.WriteLine(redirected
                ? $"[隔离] 原输出已改到静音端点「{silentSink.FriendlyName}」，可与桥接输出分开校验"
                : $"[隔离] 未能改道原输出（实际 {session.EndpointName}），目标端点回环可能混入原声");
        }

        using var capture = new ProcessLoopbackCapture((uint)child.Id);
        using var renderer = new AudioRenderer(devices, target.Id);
        using var bridge = new StreamingBridge(capture, renderer);

        var (ok, error) = bridge.Start();
        if (!ok)
        {
            Console.Error.WriteLine($"[FAIL] 管线启动失败: {error}");
            Kill(child);
            return ExitFail;
        }

        Thread.Sleep(2000);

        using var verify = new DeviceLoopbackCapture(devices, target.Id);
        var (verifyOk, verifyError) = verify.Start();
        if (!verifyOk)
        {
            Console.Error.WriteLine($"[WARN] 目标端点回环校验不可用: {verifyError}");
        }

        Thread.Sleep(500);
        _ = verify.TakeStats();
        _ = capture.TakeStats();
        Thread.Sleep(3000);
        var verifyStats = verify.TakeStats();
        var captureStats = capture.TakeStats();

        var underrunsBefore = renderer.Underruns;
        var queue = new List<double>(256);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            queue.Add(renderer.QueueMilliseconds);
            Thread.Sleep(50);
        }

        var underruns = renderer.Underruns - underrunsBefore;
        queue.Sort();

        var p50 = Percentile(queue, 0.50);
        var p95 = Percentile(queue, 0.95);
        var captureHalfMs = capture.BufferFrames / 2.0 / AudioRenderer.SampleRate * 1000;
        var renderHalfMs = renderer.BufferFrames / 2.0 / AudioRenderer.SampleRate * 1000;
        var estimate = captureHalfMs + p50 + renderHalfMs + 20;

        Console.WriteLine();
        Console.WriteLine($"[采集] 缓冲 {capture.BufferFrames} 帧（{capture.BufferFrames / 48.0:F1} ms）");
        Console.WriteLine($"[渲染] 缓冲 {renderer.BufferFrames} 帧（{renderer.BufferFrames / 48.0:F1} ms）");
        Console.WriteLine($"[队列] p50={p50:F1} ms  p95={p95:F1} ms  max={(queue.Count > 0 ? queue[^1] : 0):F1} ms");
        Console.WriteLine($"[欠载] {underruns} 次 / 10s    已渲染 {renderer.FramesRendered} 帧");
        Console.WriteLine($"[采集端] {captureStats}");
        Console.WriteLine($"[目标端点回环] {verifyStats}");
        Console.WriteLine($"[估算端到端] {estimate:F1} ms（采集缓冲一半 + 队列 + 渲染缓冲一半 + 20 ms 引擎周期，不含 DAC）");
        Console.WriteLine();

        var audioFlows = verifyStats.RmsDb > -70;
        var passed = audioFlows && underruns <= 5 && p95 < 150;
        Console.WriteLine(passed
            ? "[PASS] 流式管线可用：音频到达目标端点，队列稳定，欠载可接受"
            : "[FAIL] 流式管线未达标（见上方数据）");

        bridge.Stop();

        if (policy is not null)
        {
            foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia })
            {
                _ = policy.Set((uint)child.Id, EDataFlow.eRender, role, null);
            }
        }

        Kill(child);
        return passed ? ExitPass : ExitFail;
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Clamp(Math.Ceiling(percentile * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    // ---------------- P2：会话静音 vs 进程回环采集 ----------------

    private static int LoopbackVsMute()
    {
        Console.WriteLine("=== P2 会话静音对进程回环采集的影响 ===");
        Console.WriteLine($"[格式] {LoopbackCaptureBase.FormatDescription}");

        using var devices = new DeviceService();
        var sessions = new SessionService(devices);
        var outDir = Path.Combine(FindRepoRoot(), "artifacts", "poc");

        using var child = StartToneChild(120);
        var session = WaitForSession(sessions, (uint)child.Id, TimeSpan.FromSeconds(10));
        if (session is null)
        {
            Console.Error.WriteLine("[FAIL] 测试音进程没有创建会话");
            Kill(child);
            return ExitFail;
        }

        Console.WriteLine($"[音源] pid={child.Id} 端点={session.EndpointName} 音量={session.Volume:P0}");

        using var capture = new ProcessLoopbackCapture((uint)child.Id);
        var (ok, error) = capture.Start();
        if (!ok)
        {
            Console.Error.WriteLine($"[FAIL] 进程回环启动失败: {error}");
            Kill(child);
            return ExitFail;
        }

        Console.WriteLine("[采集] 已启动进程回环采集");

        // 预热，丢弃前 1.5s
        Thread.Sleep(1500);
        _ = capture.TakeStats();
        _ = capture.SaveWav(Path.Combine(outDir, "p2-warmup.wav"));

        var before = MeasureWindow(capture, outDir, "p2-before-mute", 2000);

        var mutedCount = sessions.SetMuteForProcess((uint)child.Id, true);
        Console.WriteLine($"[静音] 已对 {mutedCount} 个会话设置静音");
        Thread.Sleep(800);
        _ = capture.TakeStats();
        var during = MeasureWindow(capture, outDir, "p2-during-mute", 2000);

        var unmutedCount = sessions.SetMuteForProcess((uint)child.Id, false);
        Console.WriteLine($"[取消静音] 已对 {unmutedCount} 个会话取消静音");
        Thread.Sleep(800);
        _ = capture.TakeStats();
        var after = MeasureWindow(capture, outDir, "p2-after-unmute", 2000);

        Console.WriteLine();
        Console.WriteLine($"{"阶段",-14}{"RMS(dBFS)",12}{"Peak(dBFS)",12}");
        Console.WriteLine($"{"静音前",-14}{before.RmsDb,12:F1}{before.PeakDb,12:F1}");
        Console.WriteLine($"{"静音中",-14}{during.RmsDb,12:F1}{during.PeakDb,12:F1}");
        Console.WriteLine($"{"取消静音后",-14}{after.RmsDb,12:F1}{after.PeakDb,12:F1}");
        Console.WriteLine();

        var drop = before.RmsDb - during.RmsDb;
        Console.WriteLine(drop > 40
            ? "[结论] 静音会让进程回环采到静音 → B 路线不能单独使用，需要虚拟声卡配合（C 路线）"
            : $"[结论] 静音不影响进程回环采集（衰减 {drop:F1} dB）→ B 路线可单独使用，无需虚拟声卡");

        capture.Stop();
        sessions.SetMuteForProcess((uint)child.Id, false);
        Kill(child);
        Console.WriteLine($"[证据] WAV 已写入 {outDir}");
        return ExitPass;
    }

    private static CaptureStatistics MeasureWindow(LoopbackCaptureBase capture, string dir, string name, int milliseconds)
    {
        _ = capture.TakeStats();
        Thread.Sleep(milliseconds);
        var stats = capture.TakeStats();
        _ = capture.SaveWav(Path.Combine(dir, $"{name}.wav"));
        Console.WriteLine($"[窗口] {name}: {stats}");
        return stats;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "docs")) &&
                File.Exists(Path.Combine(dir.FullName, "README.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return AppContext.BaseDirectory;
    }

    // ---------------- P1b：策略写入诊断 ----------------

    private static int PolicyDiagnostics()
    {
        Console.WriteLine("=== P1b 策略写入诊断 ===");
        using var devices = new DeviceService();
        var sessions = new SessionService(devices);
        var endpoints = devices.GetRenderEndpoints();
        var target = endpoints.FirstOrDefault(e => e.IsDefaultMultimedia && e.Id != endpoints[0].Id) ?? endpoints[0];
        var alternative = endpoints.FirstOrDefault(e => e.Id != target.Id) ?? target;

        using var policy = AudioPolicyConfig.TryCreate(Console.WriteLine);
        if (policy is null) return ExitFail;

        // 1) HSTRING 往返
        using (var hs = HString.FromString(alternative.Id))
        {
            Console.WriteLine($"[HSTRING] '{HString.ToString(hs.DangerousGetHandle())}'");
        }

        // 2) 无效 PID（无音频会话）作为对照
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
        {
            var hr = policy.Set(999_999, EDataFlow.eRender, role, alternative.Id);
            Console.WriteLine($"[对照] dead pid  role={role} set -> 0x{hr:X8}");
        }

        // 3) 本进程自播自设（caller == target）
        using var self = new TonePlayer();
        self.Start();
        Thread.Sleep(800);
        var selfSessions = sessions.GetSessionsForProcess((uint)Environment.ProcessId);
        Console.WriteLine($"[自身] sessions={selfSessions.Count} endpoint={(selfSessions.Count > 0 ? selfSessions[0].EndpointName : "-")}");
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia })
        {
            var hr = policy.Set((uint)Environment.ProcessId, EDataFlow.eRender, role, alternative.Id);
            policy.Get((uint)Environment.ProcessId, EDataFlow.eRender, role, out var got);
            Console.WriteLine($"[自身] role={role} set -> 0x{hr:X8} get='{got}'");
        }

        // 4) 子进程：多角色 + 重试 + 不同设备 ID 形式
        using var child = StartToneChild(60);
        var session = WaitForSession(sessions, (uint)child.Id, TimeSpan.FromSeconds(10));
        if (session is null)
        {
            Console.Error.WriteLine("[FAIL] 子进程没有会话");
            return ExitFail;
        }

        Console.WriteLine($"[子进程] pid={child.Id} endpoint={session.EndpointName} state={session.State}");

        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var hr = policy.Set((uint)child.Id, EDataFlow.eRender, role, alternative.Id);
                policy.Get((uint)child.Id, EDataFlow.eRender, role, out var got);
                Console.WriteLine($"[重试] role={role} attempt={attempt} set -> 0x{hr:X8} get='{got}'");
                if (hr >= 0) break;
                Thread.Sleep(400);
            }
        }

        // 5) 设备 ID 形式：带前缀 / 仅 GUID / 大括号
        var guidOnly = alternative.Id.Contains("}.{") ? "{" + alternative.Id.Split("}.")[1].TrimStart('{') : alternative.Id;
        foreach (var (label, id) in new[]
                 {
                     ("full", alternative.Id),
                     ("guid-only", guidOnly),
                 })
        {
            var hr = policy.Set((uint)child.Id, EDataFlow.eRender, ERole.eMultimedia, id);
            policy.Get((uint)child.Id, EDataFlow.eRender, ERole.eMultimedia, out var got);
            Console.WriteLine($"[格式] {label}='{id}' set -> 0x{hr:X8} get='{got}'");
        }

        Kill(child);
        self.Stop();
        return ExitPass;
    }

    // ---------------- P1：策略路由端到端 ----------------

    private static int PolicyEndToEnd()
    {
        Console.WriteLine("=== P1 策略路由端到端验证 ===");
        using var devices = new DeviceService();
        var sessions = new SessionService(devices);
        var endpoints = devices.GetRenderEndpoints();

        if (endpoints.Count < 2)
        {
            Console.Error.WriteLine("[FAIL] 至少需要 2 个活动渲染端点");
            return ExitFail;
        }

        var source = endpoints.FirstOrDefault(e => e.IsDefaultMultimedia) ?? endpoints[0];
        var target = endpoints.FirstOrDefault(e => e.Id != source.Id && !e.LooksVirtual)
                     ?? endpoints.First(e => e.Id != source.Id);

        Console.WriteLine($"[设备] 默认(多媒体): {source.FriendlyName}");
        Console.WriteLine($"[设备] 目标端点    : {target.FriendlyName}");
        Console.WriteLine();

        using var policy = AudioPolicyConfig.TryCreate(Console.WriteLine);
        if (policy is null)
        {
            Console.Error.WriteLine("[FAIL] AudioPolicyConfig 不可用");
            return ExitFail;
        }

        Console.WriteLine($"[API ] IID = {policy.InterfaceId:D}");

        // 1) 启动子进程播放测试音
        using var child1 = StartToneChild(90);
        var session1 = WaitForSession(sessions, (uint)child1.Id, TimeSpan.FromSeconds(10));
        if (session1 is null)
        {
            Console.Error.WriteLine("[FAIL] 测试音进程 10s 内没有创建音频会话");
            Kill(child1);
            return ExitFail;
        }

        Console.WriteLine($"[播放] pid={session1.ProcessId} 当前端点: {session1.EndpointName}");
        Console.WriteLine($"[播放] session: {session1.SessionIdentifier}");

        // 2) 探测槽位（此时子进程确有会话，判定最可靠）
        if (!policy.TryDetectSlots((uint)child1.Id, ERole.eMultimedia, Console.WriteLine))
        {
            Console.Error.WriteLine("[FAIL] 无法探测虚表槽位");
            Kill(child1);
            return ExitFail;
        }

        // 3) 写入策略（console + multimedia 两个角色）
        var before = new Dictionary<ERole, string?>();
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia })
        {
            var hrGet = policy.Get((uint)child1.Id, EDataFlow.eRender, role, out var cur);
            before[role] = cur;
            Console.WriteLine($"[读取] role={role} hr=0x{hrGet:X8} persisted='{cur}'");

            var hrSet = policy.Set((uint)child1.Id, EDataFlow.eRender, role, target.Id);
            Console.WriteLine($"[写入] role={role} hr=0x{hrSet:X8} target='{target.Id}'");

            var hrAfter = policy.Get((uint)child1.Id, EDataFlow.eRender, role, out var after);
            var ok = hrAfter >= 0 && after == target.Id;
            Console.WriteLine($"[回读] role={role} hr=0x{hrAfter:X8} persisted='{after}' {(ok ? "OK" : "MISMATCH")}");
            if (!ok)
            {
                Console.Error.WriteLine("[FAIL] 写入后回读不一致");
                Kill(child1);
                return ExitFail;
            }
        }

        // 4) 重启音源进程，验证策略在「新会话创建」时生效
        Kill(child1);
        Thread.Sleep(600);

        using var child2 = StartToneChild(90);
        var session2 = WaitForSession(sessions, (uint)child2.Id, TimeSpan.FromSeconds(10));
        if (session2 is null)
        {
            Console.Error.WriteLine("[FAIL] 第二个测试音进程没有创建会话");
            Kill(child2);
            return ExitFail;
        }

        Console.WriteLine($"[重启] pid={session2.ProcessId} 端点: {session2.EndpointName}");
        var moved = session2.EndpointId == target.Id;
        Console.WriteLine(moved
            ? "[PASS] 策略在新会话创建时生效：音频已路由到目标端点"
            : $"[FAIL] 策略未生效：期望 {target.FriendlyName}，实际 {session2.EndpointName}");

        // 5) 清理策略（当前子进程仍在播放，可写入）
        foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia })
        {
            var hr = policy.Set((uint)child2.Id, EDataFlow.eRender, role, null);
            _ = policy.Get((uint)child2.Id, EDataFlow.eRender, role, out var cleared);
            Console.WriteLine($"[清理] role={role} hr=0x{hr:X8} persisted='{cleared}'");
        }

        Kill(child2);
        Console.WriteLine(moved ? "=== P1 结论：通过 ===" : "=== P1 结论：不通过 ===");
        return moved ? ExitPass : ExitFail;
    }

    private static Process StartToneChild(int seconds)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定测试工具路径");
        var psi = new ProcessStartInfo(exe, $"tone --seconds {seconds} --freq 997")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // 如果由 `dotnet AudioProbe.dll` 启动，则把 dll 作为第一个参数带上
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = typeof(Program).Assembly.Location;
            psi = new ProcessStartInfo(exe, $"\"{dll}\" tone --seconds {seconds} --freq 997")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        var child = Process.Start(psi) ?? throw new InvalidOperationException("无法启动测试音进程");
        Console.WriteLine($"[启动] 测试音进程 pid={child.Id}");
        return child;
    }

    private static AudioSessionInfo? WaitForSession(SessionService sessions, uint pid, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var found = sessions.GetSessionsForProcess(pid).FirstOrDefault(s => !s.IsSystemSounds);
            if (found is not null) return found;
            Thread.Sleep(120);
        }

        return null;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    // ---------------- 参数解析 ----------------

    private static string? GetStringArg(string[] args, string name, string? fallback)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    private static int GetIntArg(string[] args, string name, int fallback) =>
        int.TryParse(GetStringArg(args, name, null), out var v) ? v : fallback;

    private static double GetDoubleArg(string[] args, string name, double fallback) =>
        double.TryParse(GetStringArg(args, name, null), out var v) ? v : fallback;

    private static ERole ParseRole(string value) => value.ToLowerInvariant() switch
    {
        "console" => ERole.eConsole,
        "multimedia" => ERole.eMultimedia,
        "communications" => ERole.eCommunications,
        _ => ERole.eMultimedia,
    };

    private static string Trim(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "-" : value.Length <= max ? value : value[..max];
}
