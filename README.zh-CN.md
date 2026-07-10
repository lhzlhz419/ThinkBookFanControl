# ThinkBook 风扇控制

面向 Lenovo ThinkBook 16p G6 IAX 的实验性风扇曲线控制工具。

[English README](README.md)

## 免责声明

本项目与联想公司无关。
本项目不是联想官方项目。
本项目未获得联想公司的认可、支持或赞助。
本项目是独立的实验性工具。
本工具会通过 Lenovo WMI 接口控制风扇转速，可能影响系统散热、硬件稳定性、硬件寿命和数据安全。
使用本项目需自行承担风险；使用产生的任何后果均由使用者自行负责。
使用本工具即表示您已充分知悉上述风险并自愿承担全部后果。
若不同意，请立即停止使用并卸载本工具。

## 项目简介

这是一个 C# WPF 桌面程序。程序通过 LibreHardwareMonitor 读取 CPU、GPU、显存温度，并通过 Lenovo WMI 方法控制两个风扇。

## 当前确认的硬件接口

- WMI 命名空间：`root\wmi`
- 方法类：`LENOVO_OTHER_METHOD`
- 风扇 1 RPM / 目标转速 ID：`0x04030001`
- 风扇 2 RPM / 目标转速 ID：`0x04030002`
- 恢复自动控制的目标值：`0`
- 风扇 RPM 范围来源：`LENOVO_FAN_TEST_DATA`

## 功能

- 显示 CPU/GPU/显存温度。
- 显示风扇 1/风扇 2 转速。
- “风扇拉满”开关：先暂停当前调度并把两路手动目标恢复为 `0`，再通过
  WMI 设置 `0x04020000=1`；关闭后恢复开启前的运行/停止状态。
- CPU 和 GPU 分别设置风扇曲线。
- 每个 CPU/GPU 曲线图中分别显示风扇 1 和风扇 2 两条曲线。
- 可选择当前编辑风扇 1 或风扇 2。
- 可勾选同步转速，拖动一个风扇曲线点时同步移动另一个风扇的对应点。
- 支持 5 套配置文件。
- 支持深色/浅色主题和中文/英文界面。
- “其它设置”底部显示设备保修起止日期、在保状态和保修期进度。
- 显示设置同时支持 Vantage 护眼模式和联想电脑管家 Gamma
  护眼模式；两者互不联动。电脑管家模式提供反向色温滑块、
  可编辑 Kelvin 值以及普通/护眼两套默认值。
- 支持托盘菜单、最小化到托盘、关闭时最小化、开机自启。
- 退出程序前会先恢复固件自动风扇控制。

## 安全说明

本工具会直接写入 Lenovo 固件/WMI 风扇控制方法。目前仅针对上方硬件接口路径进行开发和测试。使用时请保持温度监控，并确认点击 `Stop` 后能够恢复自动风扇控制。

“风扇拉满”使用全局 `FNST` 开关，同时控制两把风扇。程序退出时会先
清除 `0x04020000`，再将两路手动目标写回 `0`。

运行程序需要管理员权限。

打开“其它设置”时，程序会把本机序列号发送到联想百应保修查询接口；
若查询失败，则回退到联想国际支持接口。
查询结果按设备每日缓存到
`~\.thinkbook_fan_control\warranty_cache.csharp.json`；缓存只保存序列号的
SHA-256 摘要和保修日期，不保存明文序列号。

## 构建

在仓库根目录打开 PowerShell：

```powershell
.\scripts\build_csharp.ps1 -Configuration Release -Publish
```

脚本会在 `dist` 下生成两种发布目录：

- `ThinkBookFanControl-win-x64`：自包含版本，不需要目标电脑预装 .NET 运行时。
- `ThinkBookFanControl-win-x64-net9-runtime`：体积较小，需要目标电脑已安装 .NET 9 Desktop Runtime。

构建脚本还会把本机已安装的 Vantage 显示和声音插件中实际使用的 x64
文件复制到发布目录下的 `VantageAddins`。程序运行时优先使用这些本地副本，缺失时再回退到
`C:\ProgramData\Lenovo\Vantage\Addins`。第三方插件文件不会提交到 Git。
本地副本只替代插件文件路径，相关 Lenovo 服务、驱动和音频组件仍需存在。

仓库包含电脑管家 x86 `WrapPlugin.dll`，用于调用原版
`IsSupportColorTemperature` 能力检测；实际色温计算和 Gamma Ramp
写入由程序内置实现。

更多构建说明见 [BUILDING.md](BUILDING.md)。
