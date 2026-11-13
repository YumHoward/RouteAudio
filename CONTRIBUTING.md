# 贡献指南

感谢你关注 RouteAudio。这个项目是一个 Windows 桌面应用（WPF + WASAPI），核心目标是「按进程把音频送到不同输出设备」。

## 环境要求

- Windows 10 1803+ / Windows 11（x64）
- .NET 8 SDK（`dotnet --version` 应输出 8.x）
- 可选：WiX 4（仅构建安装包时需要）

## 本地构建与测试

```powershell
dotnet build src/RouteAudio.sln -c Debug
dotnet test tests/RouteAudio.Core.Tests/RouteAudio.Core.Tests.csproj
src\RouteAudio.App\bin\x64\Debug\net8.0-windows\RouteAudio.exe
```

无人值守的 UI 冒烟检查：

```powershell
src\RouteAudio.App\bin\x64\Debug\net8.0-windows\RouteAudio.exe --smoke   # 期望输出 SMOKE OK 且退出码 0
```

打包（可选）：

```powershell
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```

## 项目结构

| 目录 | 说明 |
| --- | --- |
| `src/RouteAudio.Interop` | 唯一允许出现 P/Invoke 与 COM 定义的项目（WASAPI、策略 API） |
| `src/RouteAudio.Core` | 领域层：设备/应用模型、规则、对账式路由协调器、配置（不依赖 Windows） |
| `src/RouteAudio.Audio` | Windows 音频实现：设备/会话枚举、策略后端、回环采集、渲染 |
| `src/RouteAudio.App` | WPF 界面、托盘、设置 |
| `tools/AudioProbe` | 技术验证与排障工具（设备/会话/策略/回环/延迟） |
| `tests/RouteAudio.Core.Tests` | 单元测试 |

## 代码约定

- 分层依赖：`App → Audio → Interop`、`App → Core`；**Core 不得引用 Windows API**，除 `Interop` 外不得出现 `DllImport`。
- 音频线程内禁止分配、加锁、写日志、抛异常。
- 所有跨线程 UI 更新必须回到 UI 线程（`Dispatcher`）。
- 新增 HRESULT 处理请映射到 `RouteReason` 并给出用户可读文案。
- 提交信息使用 Conventional Commits：`feat: ...`、`fix: ...`、`docs: ...`、`chore: ...`。

## 关于系统内部 API（重要）

项目的「策略路由」依赖 Windows 未公开接口 `Windows.Media.Internal.AudioPolicyConfig`。修改相关代码时请：

1. 保留 `GetIids` 探测 + 白名单校验，未知版本必须降级而不是崩溃；
2. 保留写后回读校验；
3. 不调用 `ClearAllPersistedApplicationDefaultEndpoints`（会清除全系统所有应用的路由配置）。

## 提交 PR

1. Fork 并新建分支（`feat/xxx`）；
2. 保证 `dotnet build` 与全部单元测试通过；
3. 说明「改了什么、怎么验证的、影响范围」；
4. 涉及音频线程/COM 的改动请在 PR 中附实测说明（系统版本、音频设备型号）。
