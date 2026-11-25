# Changelog

所有重要变动记录在这里。版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [1.1.1] - 2026-11-25

### Fixed

- 从受限终端/沙箱启动时，数据目录不可写会导致启动器在没有日志的情况下运行并占住单实例互斥体，之后快捷方式启动只会“转发后退出”，看起来像没启动。现在启动时会检测数据目录可写性，失败会明确提示并退出，不再占用互斥体。
- 手动双击启动托盘并接管已有 DSH 时自动打开页面（`--autostart` / `--silent` 除外），避免出现“托盘起来了但看不到页面”的错觉。

## [1.1.0] - 2026-11-25

### Changed

- 退出托盘不再停止 DSH：只关闭托盘图标，DSH 继续在后台运行。
- DSH 的 stdout/stderr 改为直接写入 `dsh-web.log`，不再经过托盘进程的管道，托盘退出不会影响 DSH 进程。
- 退出时保留 `state.ini`；下一次启动托盘会根据 PID 和 token 地址自动接管正在运行的 DSH。
- 托盘菜单「退出」改为「退出（DSH 继续运行）」。

### Added

- 进程存活检测与终止改用 Win32 API（`OpenProcess` / `GetExitCodeProcess` / `TerminateProcess`），在受限环境下也能可靠停止 DSH。
- `DSHTRAY_DEBUG=1` 调试日志、`DSHTRAY_INSTANCE` 隔离实例（便于并行调试）。

## [1.0.1] - 2026-11-25

### Fixed

- 修复点击「停止 DSH / 重启 DSH」后启动器栈溢出退出的问题（`DeleteStateFile` 误写成自递归）。
- `taskkill` 失败时改用进程句柄兜底终止。
- CLI 的 `--stop` / `--restart` 会等待操作真正完成。

### Added

- `--restart` 子命令。

## [1.0.0] - 2026-11-25

### Added

- 首个版本：托盘启动/停止/重启 DSH，单实例，自动接管已运行实例，托盘状态颜色，安装/卸载脚本。
