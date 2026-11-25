# DeepSeek Harness 托盘启动器

[English](README.md) | **简体中文**

Windows 托盘小工具：双击启动 `dsh web`，托盘图标显示运行状态，可打开页面、重启、停止，并避免重复启动。

[下载最新 Release](https://github.com/CAgAG/dsh_tray/releases/latest)

## 功能

- 双击即启动 DSH（等价 `npx @deepseek-ai/dsh web`，没有命令行窗口）
- 托盘图标显示状态：绿=运行中，蓝=外部实例，黄=启动/停止中，红=失败，灰=已停止
- 左键/双击打开页面（自动使用启动时的 token 地址，避免 401）
- 右键菜单：打开页面、复制地址、重启、停止、查看日志、编辑配置、开机自启、退出
- 单实例；已有 DSH 时自动接管，不会重复启动
- 退出托盘只关图标，DSH 继续运行，下次启动自动接管

## 环境要求

- Windows 10/11
- Node.js ≥ 24.9
- 至少成功运行过一次 `npx @deepseek-ai/dsh web`

## 安装与使用

1. 运行 `install.cmd`（或双击 `dist\DeepSeekHarnessTray.exe`）。
2. 双击桌面「DeepSeek Harness」。
3. 首次运行在 `%LOCALAPPDATA%\DeepSeekHarnessTray` 生成配置与日志。

## 配置与日志

- 配置：`%LOCALAPPDATA%\DeepSeekHarnessTray\config.ini`（端口、监听地址、自动打开页面等）
- 界面语言：`config.ini` 里 `language=auto`（跟随系统）/ `en` / `zh`
- 日志：`launcher.log`（启动器）、`dsh-web.log`（DSH 输出）
- 停止 DSH：托盘菜单「停止 DSH」，或运行 `DeepSeekHarnessTray.exe --stop`
- 其他命令：`--status`、`--diagnose`、`--start`、`--stop`、`--restart`、`--quit`

## 从源码构建

需要 Windows 自带的 .NET Framework 4.x（含 `csc.exe`）：

```bat
build.cmd
```

产物：`dist\DeepSeekHarnessTray.exe`。

## 发布

```bat
.\bump-version.cmd 1.2.0
git add -A
git commit -m "release: v1.2.0"
.\release.cmd 1.2.0 -Push
```

`-Push` 推送 tag，由 GitHub Actions 构建 exe 并创建 Release；本地直接发布可用 `-Manual`。

## 常见问题

- **点快捷方式没反应**：确认任务管理器里没有残留的 `DeepSeekHarnessTray`；不要从 DSH 终端或沙箱里运行 `dist` 下的 exe。
- **页面提示需要验证 / 401**：当前是命令行启动的外部实例，用托盘菜单「重启 DSH」接管。
- **端口被占用**：会自动改用随机端口，也可在 `config.ini` 修改 `port`。

版本历史见 [CHANGELOG.md](CHANGELOG.md)。
