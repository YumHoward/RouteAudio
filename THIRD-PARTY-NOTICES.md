# 第三方组件与许可

本项目自身代码以 [MIT](LICENSE) 许可发布。以下是构建/测试期使用、或运行时依赖的第三方组件说明。

## 运行时依赖

| 组件 | 用途 | 许可 | 说明 |
| --- | --- | --- | --- |
| .NET 8 Desktop Runtime | 运行时（框架依赖发布） | MIT | 由用户系统提供；自包含发布时会打包 |
| Windows 系统组件（WASAPI / Core Audio / MMCSS） | 音频设备、会话、回环、调度 | 系统组件 | 通过操作系统提供的公开与内部接口调用 |

> 项目**不打包、不分发**任何第三方音频驱动。界面上识别到的虚拟音频设备（如 VB-CABLE、VoiceMeeter、UU远程虚拟声卡、NVIDIA Broadcast）由用户自行安装，其许可与支持由各自厂商负责。

## 构建期工具（不随产物分发）

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| WiX Toolset v4 | 生成 MSI 安装包 | MS-RL |
| Microsoft.Windows.CsWin32 / Win32 元数据 | 传统 P/Invoke 参考 | MIT |
| .NET SDK / MSBuild | 构建 | MIT |

## 测试依赖（不进入发布产物）

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| xunit | 单元测试框架 | Apache-2.0 |
| xunit.runner.visualstudio | 测试运行器 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 测试宿主 | MIT |
| BenchmarkDotNet（预留） | 基准测试 | MIT |

## 字体与图标

- 界面字体：Microsoft YaHei UI、Segoe UI（系统字体）。
- 图标字形：Segoe Fluent Icons / Segoe MDL2 Assets（系统字体），通过字符码使用，不再分发字体文件。
