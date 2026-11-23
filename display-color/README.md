# 三屏调色恢复资料

版本：2026-11-23 最终采用版，3 号 Gamma 1.20。

## 当前参数

顺序均为 R / G / B，数值使用 NVIDIA App 原始刻度。

| 屏幕 | 亮度 | 对比度 | Gamma |
|---|---|---|---|
| 2，下方 ARZOPA 横屏 | 98 / 100 / 94 | 96 / 100 / 88 | 1.08 / 1.08 / 1.08 |
| 3，右侧 ARZOPA 竖屏 | 89 / 100 / 84 | 84 / 100 / 84 | 1.20 / 1.20 / 1.20 |

数字亮丽 50%，色调 0。两块机身背光已由用户设为 100%。1 号笔记本内屏作为参照，恢复工具不向它应用配置。

profile.json 同时包含每块外接屏的 Windows 基础曲线，格式是三个连续的 256 项 uint16 数组，顺序 R、G、B。需要同时保留这层基线与 NVIDIA 目标 Gamma 调整。

## 已启用与已验证

- 当前用户登录任务：Jyu_DisplayColorGuard，登录后延迟 15 秒。
- 无控制台窗口运行；每 10 秒检查，正常桌面每 60 秒兜底应用 NVIDIA 目标曲线。
- 按 EDID 身份匹配目标；NVIDIA 原生 ID 用于定位实际输出与保存参数的注册表键。用户可见编号不等于 GDI 内部编号。
- 在锁屏、无边框全屏或高级颜色/HDR 场景让出控制；恢复普通 SDR 桌面后继续。
- 编译、绑定、恢复调用、基础曲线回读、参照屏未改变均已验证。
- 已实测临时 1% GDI 曲线扰动和 1 单位保存 Gamma 扰动，由守护进程恢复；结果附有守护进程自身写入的证据。
- 未执行整机重启测试；真实睡眠唤醒与拔插场景未逐项触发测试。

NVIDIA 私有目标曲线接口缺少对应的受支持回读接口，因此工具以保存的 RGB 参数重建 1024 点目标曲线，检查驱动接受返回值，并以基础曲线回读、保存参数恢复和守护进程证据进行验证。这些检查不等同于用校色仪测量或完整的硬件像素输出验证。

## 本机使用

优先使用本机备份目录中现存的资料：

- display-color-approved.json：当前采用的参数及 NVIDIA 保存值。
- display-color-runtime.json：本用户、两块显示器身份、精确 Windows 基线、重建的 NVIDIA 目标曲线。
- display-color-validation.json：验证结果；display-color-status.json：最近状态。
- display-color-last-recovery.json：最近一次实际漂移恢复证据。

同目录命令：

- Restore-DisplayColor.cmd：立即恢复。
- Pause-DisplayColorGuard.cmd / Resume-DisplayColorGuard.cmd：暂停 / 恢复。
- Disable-DisplayColorGuard.cmd：停止守护并删除它自己的登录任务，保留当前颜色及资料。
- Enable-DisplayColorGuard.cmd：用 Windows 自带的 .NET Framework C# 编译器编译源码，注册并启动登录任务。

DisplayColorGuard.cs 与命令文件要放在本机配置文件所在目录。原有绑定快照不会自动被覆盖。移动目录后应从新位置重新启用。

## 给下次处理此配置的助手

1. 先阅读此页和 profile.json，核对版本为 3 号 Gamma 1.20。
2. 优先读取本机满意版备份，确认任务和守护进程状态，再执行同目录的恢复命令。
3. 不把用户显示编号当作 GDI 编号。需要重新绑定时运行工具 inspect，在本机核实 NVIDIA ID、保存键与 EDID 身份。
4. 若本机运行快照遗失，公开 JSON 已保留两屏的 RGB 参数和 Windows 基线。根据本机实际目标身份重建私有配置；不要从已经掉色的画面重新采样并覆盖满意版基线。
5. 改色前先暂停守护。新版本经用户认可后，更新参数备份，归档旧运行快照并重新捕获/验证，再恢复守护。调色参数变化会使旧快照摘要失效。
6. 遵守当次会话对本机代码执行、后台运行和发布的授权要求。此公开说明不代替用户授权。
7. 本机用户 SID、显示器序列号、设备路径及完整运行快照和日志留在本机，不加入公开提交。

## 实现与授权

Windows GDI 基线使用已保存的实际 256 项曲线。NVIDIA 部分使用 NvAPI_DISP_SetTargetGammaCorrection，其 1024 点、RGB 交错目标曲线计算及接口协议移植自：

- [pbatard/nvBrightness](https://github.com/pbatard/nvBrightness)，Copyright © 2025 Pete Batard，GPL-3.0-or-later。
- 工具源码按 [GPL-3.0-or-later](LICENSE.txt) 分发，许可证仅针对这个工具及其相关派生代码。
- [NVIDIA 官方颜色模式说明](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nvdsp/CS_Adjust_Color_Settings_Advanced.htm)。
- [Windows Gamma Ramp API 限制](https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp)。

已安装驱动可接受该接口；驱动更新后应重新验证。HDR、独占全屏和第三方颜色工具可能覆盖调色，守护采取让出与恢复策略，不保证这些场景中持续强制生效。
