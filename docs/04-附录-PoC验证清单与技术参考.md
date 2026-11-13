# 04 · 附录：PoC 验证清单与技术参考

| 项 | 内容 |
| --- | --- |
| 文档编号 | RA-APPX-001 |
| 版本 | v1.0 |
| 日期 | 2026-11-08 |
| 关联 | [01 需求](01-需求规格说明书.md) · [02 架构](02-技术方案与架构设计.md) · [03 实施](03-项目结构与实施计划.md) |

---

## A. PoC 验证清单（编码前的 Gate）

> 目标：用 **2 周、一台 Win10 21H2 + 一台 Win11 24H2 实机**，把三个高风险假设验证到「结论明确」。每条都要有可复现的脚本或工具，结论写回本表。
> 判定原则：**任何 P1 或 P2 失败，都必须先改架构再开工**，不允许「边写边看」。

### P1 —— 策略 API 可用性与持久化（最高优先级）

| 项 | 内容 |
| --- | --- |
| 假设 | `SetPersistedDefaultAudioEndpoint` 在同用户进程、无管理员权限下可写入，且设置在应用重启/系统重启后保持 |
| 方法 | 1) 控制台程序：`RoGetActivationFactory` → `GetIids` 打印全部 IID，与白名单比对；2) `Set(pid → 目标端点)` 后 `Get` 回读；3) 播放正弦，用另一端点回环确认声音位置；4) 关闭应用重开、重启系统后再测；5) 在 Windows 设置里改同一应用，观察我们的 `Get` 是否同步 |
| 通过标准 | Win10 21H2 与 Win11 24H2 均能写入并回读一致；应用重启后仍生效；系统重启后仍生效（或明确「需一次重写」并记录） |
| 失败对策 | 全面转 C 路线（虚拟声卡），产品定位调整为「虚拟声卡编排器」 |
| 产出 | `tools/AudioProbe` 的 `policy-probe` 子命令 + 结果表 |

### P2 —— 会话静音是否影响进程回环采集（决定 B 路线生死）

| 项 | 内容 |
| --- | --- |
| 假设 | 对某进程的音频会话调用 `SetMute(true)`（或音量 0）后，`Application Loopback` 采集**仍能**拿到非静音数据 |
| 方法 | 1) 用测试进程播放 1 kHz 正弦到默认端点；2) 启动进程回环采集，录制 3 s，测 RMS；3) 静音该进程会话，再录 3 s，测 RMS；4) 解除静音，再录 3 s；5) 同时用设备回环对照（预期设备回环会静音） |
| 通过标准 | 静音前后采集 RMS 差异 < 0.5 dB → B 路线可单用；若静音后 RMS < −80 dBFS → B 路线必须搭配虚拟声卡 |
| 失败对策 | 采用「策略把应用指向虚拟端点 → 采集虚拟端点 → 渲染到物理端点」；B 退化为 C 的采集组件 |
| 产出 | 三种状态的 WAV + RMS 表 + 结论 |

### P3 —— 流式延迟与稳定性

| 项 | 内容 |
| --- | --- |
| 假设 | 端到端 P95 ≤ 50 ms，且 1 h 无爆音 |
| 方法 | 用 `IAudioClient::GetDevicePosition`/QPC 记录采集与渲染时间戳；独立通道播放脉冲串（10 ms 间隔），在目标设备用另一台机器/声卡回录，互相关求延迟；同时统计 underrun/overrun |
| 通过标准 | P50 ≤ 30 ms、P95 ≤ 50 ms、1 h 内 underrun = 0 |
| 失败对策 | 降缓冲至 5 ms、提高 MMCSS 优先级、限制并发路数；若仍不达标，产品文档明确「流式仅用于非实时场景」 |

### P4 —— 采样率不一致与时钟漂移

| 项 | 内容 |
| --- | --- |
| 假设 | 44.1 kHz 采集 → 48 kHz 渲染，持续 60 min 无累积漂移、无爆音 |
| 方法 | 采集端 44.1 kHz、渲染端 48 kHz；每 100 ms 记录队列水位；连续 60 min；统计水位漂移斜率与校正次数 |
| 通过标准 | 水位稳定在目标 ±10 ms；校正次数 < 5 次/min；无 underrun |
| 失败对策 | 改进 `DriftCorrector`（改比率步长/死区）；或强制重采样到统一采样率 |

### P5 —— 独占模式行为

| 项 | 内容 |
| --- | --- |
| 方法 | 用支持独占模式的播放器（如 foobar2000 WASAPI 独占）占用目标端点；再尝试策略路由与流式路由 |
| 期望 | 策略：应用仍走独占端点（无效），UI 显示 RA-004/RA-005；流式：采集失败，给 RA-005；**不得崩溃、不得静音原播放** |
| 通过标准 | 所有分支都有原因码与用户文案 |

### P6 —— UWP / 商店应用

| 项 | 内容 |
| --- | --- |
| 方法 | Spotify（Store 版）、Netflix、Windows 媒体播放器；分别测策略路由与流式 |
| 观察 | `IAudioSessionControl2::GetSessionIdentifier` 内容；策略是否按 AUMID 生效；DRM 内容是否静音 |
| 通过标准 | 至少策略路由对非 DRM 的 Store 应用生效；DRM 场景给 RA-006 |

### P7 —— 受保护内容（DRM）

| 项 | 内容 |
| --- | --- |
| 方法 | 播放 Netflix / Prime Video（浏览器或应用），尝试流式采集 |
| 期望 | 采集到静音或失败；**不得**输出噪声/错误内容；明确提示「系统限制」 |
| 通过标准 | UI 文案正确，原播放不中断 |

### P8 —— 蓝牙与 HDMI 设备

| 项 | 内容 |
| --- | --- |
| 方法 | 蓝牙耳机连接/断开/自动关机；HDMI 显示器休眠/唤醒 |
| 观察 | 端点 ID 是否变化；事件风暴是否被去抖；路由是否恢复 |
| 通过标准 | 断开 2 s 内回退并通知；恢复后自动回原设备 |

### P9 —— 多会话环境

| 项 | 内容 |
| --- | --- |
| 方法 | RDP 登录、快速用户切换、锁屏 |
| 观察 | 端点是否重定向；事件是否重复；路由是否误报失败 |
| 通过标准 | 不误报；切换后 5 s 内自愈 |

### P10 —— 安全软件与反作弊共存

| 项 | 内容 |
| --- | --- |
| 方法 | 在装有主流杀软（Defender + 1 款第三方）与 1 款带反作弊的游戏下运行 |
| 观察 | 是否拦截 `ActivateAudioInterfaceAsync`；游戏是否异常 |
| 通过标准 | 无拦截；若被拦截，有明确提示与白名单说明 |

### PoC 结论记录表

> 2026-11-08 首轮实测（Win11 build 26300，完整记录见 [05-PoC 结果报告](05-PoC结果报告.md)）

| Gate | 平台 | 结论 | 关键数据 | 日期 |
| --- | --- | --- | --- | --- |
| P1 | Win11 26300 | ✅ 通过 | IID `ab3d4648-…`；槽位 25/26/27；设备接口路径格式；`Set` 后重启目标进程音频即进入目标端点 | 2026-11-08 |
| P2 | Win11 26300 | ❌ 不通过 | 静音前 −17.6 dBFS → 静音中 **−96.7 dBFS** → 取消后 −17.6 dBFS | 2026-11-08 |
| C1（新增） | Win11 26300 | ✅ 通过 | 策略→虚拟端点→设备回环采集 −20.0 dBFS；物理端点会话数 0（无双声） | 2026-11-08 |
| P3 | — | ⏳ 待测 | 需要渲染管线 | — |
| P4–P10 | — | ⏳ 待测 | — | — |

**P1 的两个实现要点（已写入代码与 02 文档）**

1. `deviceId` 必须传设备接口路径，不是端点 ID：
   `\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{e6327cad-dcec-4949-ae8a-991e976a79d2}`
2. 传空字符串清除覆盖；**禁止**调用 `ClearAllPersistedApplicationDefaultEndpoints`（会清掉全系统所有应用覆盖）。

**P2 对架构的影响**：B 路线从「搬移兜底」降级为「镜像/录制」；「搬移」的兜底改由 C 路线（虚拟端点）承担。

---

## B. 关键 API 速查

### B.1 设备与端点

| API | 用途 | 备注 |
| --- | --- | --- |
| `CoCreateInstance(CLSID_MMDeviceEnumerator)` → `IMMDeviceEnumerator` | 枚举端点 | 每个线程需 `CoInitializeEx`；建议整个应用一个 MTA 线程 + UI 线程 STA |
| `IMMDeviceEnumerator::EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)` | 活动输出端点 | 也支持 `DEVICE_STATEMASK_ALL` 查禁用设备 |
| `IMMDevice::GetId()` | 端点 ID | 形如 `{0.0.0.00000000}.{guid}`；策略 API 需要原样字符串 |
| `IMMDevice::OpenPropertyStore(STGM_READ)` | 名称/外形/容器 ID | `PKEY_Device_FriendlyName`、`PKEY_AudioEndpoint_FormFactor`、`PKEY_Device_ContainerId` |
| `IMMDeviceEnumerator::GetDefaultAudioEndpoint(eRender, role)` | 三角色默认端点 | 角色：`eConsole` / `eMultimedia` / `eCommunications` |
| `IMMDeviceEnumerator::RegisterEndpointNotificationCallback` | 热插拔 | 回调在系统 MTA 线程，禁止回调内重入 MMDevice |
| `IMMDevice::Activate(IID_IAudioSessionManager2)` | 会话管理 | 每端点一个 |
| `IMMDevice::Activate(IID_IAudioClient)` | 打开音频流 | 共享/独占；事件驱动需 `SetEventHandle` |

### B.2 会话与音量

| API | 用途 | 坑 |
| --- | --- | --- |
| `IAudioSessionManager2::GetSessionEnumerator` | 枚举该端点上的会话 | 只覆盖**当前端点**，要全量需遍历所有端点 |
| `IAudioSessionControl2::GetProcessId` | 会话所属进程 | 系统声音会话返回特殊值 |
| `IAudioSessionControl2::GetSessionIdentifier` | 会话标识 | UWP 含包信息，可用于识别商店应用 |
| `IAudioSessionControl2::IsSystemSoundsSession` | 是否系统声音 | 默认从 UI 隐藏 |
| `IAudioSessionControl::RegisterAudioSessionNotification` | 会话事件 | 回调轻量化，勿做重活 |
| `ISimpleAudioVolume::SetVolume/SetMute` | 会话音量/静音 | 与系统混音器同步；**注意 P2 结论** |
| `IChannelAudioVolume` | 通道路由/音量 | 可做简单声道映射（v2） |

### B.3 策略路由（内部 API）

| 项 | 值 |
| --- | --- |
| 激活类名 | `Windows.Media.Internal.AudioPolicyConfig` |
| 取用 | `RoGetActivationFactory` → `IInspectable::GetIids` → `QueryInterface` 到白名单 IID |
| 方法 | `GetPersistedDefaultAudioEndpoint` / `SetPersistedDefaultAudioEndpoint` / `ClearAllPersistedApplicationDefaultEndpoints` |
| 参数 | `(uint processId, EDataFlow eRender, ERole, HSTRING deviceId)`；deviceId 为**设备接口路径**（见下），空串表示清除 |
| 设备接口路径格式 | `\\?\SWD#MMDEVAPI#{端点ID}#{e6327cad-dcec-4949-ae8a-991e976a79d2}`（渲染）／`#{2eef81be-33fa-4800-9670-1cd474972c3f}`（采集） |
| 错误码 | `E_INVALIDARG (0x80070057)` = 设备 ID 格式错误 **或** 进程当前无音频会话（需分开判断） |
| 特殊返回 | `PROCESS_NO_AUDIO`：该进程暂无音频会话（等待后重试） |
| 已知 IID | `32aa8e18-…`（1709/1803）、`2a59116d-…`（20H1–21H1）、`ab3d4648-…`（Win11/21H2+） |
| 风险 | 非公开契约；虚表槽位随版本不同；不可用于 Microsoft Store 上架 |

### B.4 进程回环与流式

| API / 常量 | 用途 |
| --- | --- |
| `ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, IID_IAudioClient, VT_BLOB(AUDIOCLIENT_ACTIVATION_PARAMS), handler, out op)` | 激活进程回环客户端 |
| `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` | 激活类型 |
| `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` | 含子进程 |
| `AUDCLNT_STREAMFLAGS_LOOPBACK` | 回环采集 |
| `AUDCLNT_STREAMFLAGS_EVENTCALLBACK` | 事件驱动（必需，避免轮询） |
| `AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM` + `SRC_DEFAULT_QUALITY` | 引擎代做重采样 |
| `IAudioCaptureClient::GetNextPacketSize/GetBuffer/ReleaseBuffer` | 采集循环 |
| `IAudioRenderClient::GetBuffer/ReleaseBuffer` | 渲染循环 |
| `AvSetMmThreadCharacteristics("Pro Audio")` + `AvSetMmThreadPriority(AVRT_PRIORITY_CRITICAL)` | 音频线程提权 |
| `IAudioClient3` | 更低周期（低延迟共享模式），需硬件/驱动支持；Win10 起提供 |

### B.5 最小版本

| 能力 | 最低系统 |
| --- | --- |
| 共享模式 WASAPI / 会话 API | Windows Vista（Vista 起） |
| `IAudioClient3` | Win10 1703 |
| 策略路由（内部 API） | Win10 1709/1803（实践） |
| 进程回环 | Win10 2004（build 19041）+（官方结构文档标注 Build 20348） |
| `ActivateAudioInterfaceAsync` | Win8 |

---

## C. 参考资料

**官方文档与示例**

- [AUDIOCLIENT_ACTIVATION_PARAMS（进程回环参数结构）](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_activation_params)
- [ActivateAudioInterfaceAsync](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-activateaudiointerfaceasync)
- [官方示例：ApplicationLoopback（C++ 采集指定进程音频）](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/ApplicationLoopback)
- [Core Audio APIs 总览](https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-apis-in-windows-vista)
- [Audio Sessions（会话管理）](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions)
- [IMMNotificationClient（设备事件）](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient)
- [Multimedia Class Scheduler Service（MMCSS）](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service)
- [Low Latency Audio](https://learn.microsoft.com/en-us/windows/win32/coreaudio/low-latency-audio)
- [HSTRING 管理](https://learn.microsoft.com/en-us/windows/win32/api/winstring/)
- [RoGetActivationFactory](https://learn.microsoft.com/en-us/windows/win32/api/roapi/nf-roapi-rogetactivationfactory)
- [虚拟音频驱动示例（sysvad / AVADriver，v2 参考）](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/virtual-audio-device-driver-samples)

**开源参考实现（阅读其互操作与版本适配思路）**

- [EarTrumpet（按应用音量与设备选择）](https://github.com/File-New-Project/EarTrumpet)
- [SoundSwitch（默认设备切换 + AudioPolicyConfig 互操作）](https://github.com/Belphemur/SoundSwitch)
- [Audio Router（早期按应用路由，已归档）](https://github.com/audiorouterdev/audio-router)
- [NAudio（WASAPI 包装，可作对照与测试工具）](https://github.com/naudio/NAudio)

**第三方虚拟声卡**

- [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) —— 仅作为可选依赖检测，不打包分发

**协议与规范**

- [Conventional Commits](https://www.conventionalcommits.org/)
- [Semantic Versioning](https://semver.org/)

---

## D. 术语表（补充）

| 术语 | 说明 |
| --- | --- |
| MMCSS | 多媒体类调度服务，为音频线程提供高于普通线程的实时优先级 |
| APO | Audio Processing Object，端点上的音效处理链，无法用于「改道」 |
| KS | Kernel Streaming，WASAPI 之下的内核流接口 |
| mix format | 端点的混音格式（通常 float32 / 48 kHz / 2ch），共享模式下由引擎维护 |
| underrun / overrun | 缓冲欠载（无数据可播）/ 溢出（数据丢失），表现为爆音或卡顿 |
| 对账（reconcile） | 周期性把「期望状态」与「实际状态」比对并修复差异的机制 |
| 容器 ID | 设备容器标识，同一物理设备的多个功能共享，可用于重插后识别 |

---

## E. 待定问题（与 01 §12 对应）

| ID | 问题 | 需要的实验/决策 | 期限 |
| --- | --- | --- | --- |
| OQ-01 | 静音是否影响进程回环 | P2 | 第 1 周 |
| OQ-02 | 策略持久化边界 | P1 | 第 1 周 |
| OQ-03 | UWP 策略生效方式 | P6 | 第 2 周 |
| OQ-04 | 同 exe 多实例是否能差异化 | 试验：两个同路径实例，分别 Set，观察是否互相影响 | v1.1 |
| OQ-05 | 是否自研虚拟声卡 | 成本评估（驱动开发 + 签名 + 测试） | v2.0 |
| OQ-06 | 能否通过 Microsoft Store | 用 MSIX 打包试提交（内部） | v1.1 |
| OQ-07 | 默认是否开启流式兜底 | 依据 P3 延迟数据与用户调研 | M4 |
| OQ-08 | 是否需要「按窗口」路由 | 产品调研；技术上需注入，明确不做 | v1.1 |

---

## F. PoC 报告模板

```markdown
# PoC 结论：P{n} {名称}

- 日期 / 执行人 / 平台（版本号、音频设备型号）
- 假设：
- 方法（可复现步骤、脚本路径）：
- 原始数据（表格/截图/WAV 路径）：
- 结论：通过 / 不通过 / 有条件通过（条件：…）
- 对架构的影响（需要修改的章节）：
- 遗留风险与后续动作：
```
