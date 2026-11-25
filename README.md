# DeepSeek Harness 托盘启动器 (DSHTray)

双击一个 exe 就能启动 DSH Web，不用再开命令行敲 `npx @deepseek-ai/dsh web`：

- 托盘图标显示 DSH 是否在运行（颜色 + 悬浮提示 + 菜单状态行）
- 打开页面时自动带上 DSH 启动时打印的 `?token=...` 地址（否则直接访问会 401）
- 菜单里可以打开页面、重启、停止 DSH
- 防重复启动：托盘自身单实例；目标端口已有 DSH 时直接接管，不会再起第二个
- 可选开机自启

> 这是给本机 DSH 配的个人启动器，不是 DSH 官方组件。它不修改 DSH 本身，只是替你在后台运行 `dsh web` 并管理它的进程。

---

## 快速开始

1. 双击 `dist\DeepSeekHarnessTray.exe`（或先运行 `install.cmd` 安装并创建快捷方式）。
2. 托盘出现绿色图标，DSH 启动完成后浏览器自动打开页面。
3. 以后只用托盘：左键单击 = 打开页面；右键 = 全部操作。

第一次运行会在 `%LOCALAPPDATA%\DeepSeekHarnessTray` 生成配置、日志和状态文件。

---

## 环境要求

- Windows 10 / 11（x64）
- .NET Framework 4.8（Win10/11 自带，无需安装）
- Node.js ≥ 24.9（当前 DSH 的 `engines` 要求）
- 至少成功运行过一次 `npx @deepseek-ai/dsh web`（npx 缓存或全局安装里能找到 `@deepseek-ai/dsh`）

启动器会自己找 `node.exe` 和 `@deepseek-ai/dsh` 的位置（PATH、`%ProgramFiles%\nodejs`、nvm/fnm/Volta、`%LOCALAPPDATA%\npm-cache\_npx\*`、npm 全局目录），并选择版本最高的那个。找不到时才回退到 `npx @deepseek-ai/dsh@latest`（会联网下载）。

---

## 托盘菜单

| 菜单项 | 作用 |
| --- | --- |
| `DSH：运行中 · http://127.0.0.1:3080` | 状态行（不可点），显示当前状态和地址 |
| 打开页面 / 启动 DSH | 运行中时打开带 token 的地址；已停止时启动并打开 |
| 复制页面地址 | 把当前地址（优先带 token）复制到剪贴板 |
| 重启 DSH | 结束当前进程树后重新启动（外部实例会先确认） |
| 停止 DSH | 确认后结束整个 DSH 进程树 |
| 查看日志 | 用默认编辑器打开 `launcher.log` |
| 编辑配置 | 打开 `config.ini` |
| 打开数据文件夹 | 在资源管理器里打开数据目录 |
| 开机自动启动 | 勾选后写入 `HKCU\...\Run`，登录 Windows 时自动启动托盘 |
| 退出（DSH 继续运行） | 只关闭托盘，DSH 保持运行；下次打开托盘会自动接管 |

左键单击或双击托盘图标都是「打开页面」（未运行时启动）。托盘提示里也会显示 PID 和地址。

### 图标颜色

| 颜色 | 含义 |
| --- | --- |
| 绿色 | 运行中（由本启动器启动/管理） |
| 蓝色 | 运行中，但是外部实例（例如你在命令行里启动的 DSH） |
| 黄色 | 启动中 / 正在停止 |
| 红色 | 启动失败 / 端口被其他程序占用 |
| 灰色 | 已停止，点击即可启动 |

---

## 行为细节

### 防重复启动

- 启动器自身是单实例：再次双击 exe，只会把请求转给已运行的托盘（默认打开页面；带 `--silent` 时只刷新状态），第二个进程立即退出。
- 启动前会先探测目标端口：如果已经有 DSH 在跑，直接接管（蓝色图标），不会再启动一个。
- 如果目标端口被**其他程序**占用，默认自动改用系统分配的随机端口（菜单和日志里会显示实际端口），不会报错卡住。

### 带 token 的页面地址

DSH 每次启动都会生成一个进程内 token，并在终端打印形如：

```
dsh web: http://127.0.0.1:3080/?token=xxxxxxxx
```

直接用 `http://127.0.0.1:3080` 打开会得到 401。启动器会捕获这行输出，打开页面时使用带 token 的地址，所以不需要你手动复制。

### 外部实例（命令行启动的 DSH）

如果你现在的 DSH 是在命令行里启动的，启动器会识别为「外部实例」：

- 不会杀掉或重复启动它；
- 「打开页面」打开的是不带 token 的地址。你的浏览器如果之前访问过带 token 的地址（cookie 有效期 30 天），可以正常打开；否则会看到 401 提示。
- 想彻底交给托盘管理：菜单 →「重启 DSH」，确认后会结束外部进程，并由托盘重新启动（生成新的 token 地址）。
- 退出托盘**不会**停止任何 DSH（无论是托盘启动的还是外部实例）；只有「停止 DSH」或「重启 DSH」才会动它。

### 退出与停止

- **退出**只关闭托盘图标，DSH 继续在后台运行，页面不会断。下次双击快捷方式启动托盘时，会通过 `state.ini` 里的 PID 和 token 地址自动接管正在运行的实例。
- 想真正关闭 DSH：托盘菜单 →「停止 DSH」；或者退出托盘后执行 `DeepSeekHarnessTray.exe --stop`。
- DSH 的 stdout/stderr 直接写入 `dsh-web.log` 文件，不再经过托盘进程的管道，所以托盘退出不会影响 DSH 进程。
- 停止使用 `taskkill /T /F` 结束整个进程树，被结束时 DSH 的退出码为 1，属于正常现象。

---

## 配置文件

位置：`%LOCALAPPDATA%\DeepSeekHarnessTray\config.ini`（首次运行自动生成）。修改后请退出托盘再重新打开（「重启 DSH」只重启进程，不会重读配置）。

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `port` | `3080` | 监听端口；填 `0` 表示由系统分配随机端口 |
| `host` | `127.0.0.1` | 监听地址，单个本机地址 |
| `profile` | `web` | DSH profile，对应 `dsh web` |
| `extraArgs` | 空 | 追加给 `dsh web` 的参数，例如 `--public-url https://dsh.example.com/` |
| `nodePath` | 空 | 手动指定 `node.exe` 完整路径 |
| `dshEntry` | 空 | 手动指定 `@deepseek-ai/dsh` 包目录或 `lib\bin.js` |
| `workingDir` | 空 | DSH 进程工作目录；空 = 用户主目录 |
| `openOnReady` | `true` | DSH 就绪后自动打开页面 |
| `startOnLaunch` | `true` | 打开托盘时自动启动 DSH |
| `fallbackRandomPort` | `true` | 端口被占用时自动改用随机端口 |
| `launchTimeoutSeconds` | `240` | 启动超时（插件多、首次安装时可能较慢） |

实际启动命令类似：

```
"C:\Program Files\nodejs\node.exe" "…\@deepseek-ai\dsh\lib\bin.js" web --no-open --host 127.0.0.1 --port 3080
```

`--no-open` 让 DSH 不自己弹浏览器，由启动器在就绪后打开正确的 token 地址。

---

## 日志与数据文件

目录：`%LOCALAPPDATA%\DeepSeekHarnessTray`

| 文件 | 内容 |
| --- | --- |
| `config.ini` | 配置 |
| `launcher.log` | 启动器日志（识别的 node/DSH 版本、状态变化、错误） |
| `dsh-web.log` | DSH 进程的原始 stdout/stderr，由 DSH 直接写入（托盘退出后仍继续） |
| `state.ini` | 当前托管进程的 PID 和带 token 的地址（仅本机可读，停止 DSH 时删除） |
| `status.ini` | 托盘实时状态快照，`--status` 读取它 |
| `cli-output.txt` | 最近一次命令行子命令的输出 |

---

## 命令行用法（可选）

给脚本或快捷方式用：

```text
DeepSeekHarnessTray.exe                    启动托盘（默认行为）
DeepSeekHarnessTray.exe --autostart        开机自启模式（不自动弹浏览器）
DeepSeekHarnessTray.exe --status           输出当前状态；运行中退出码 0，否则 1
DeepSeekHarnessTray.exe --diagnose         输出诊断信息（node/DSH 解析、探测结果、状态文件）
DeepSeekHarnessTray.exe --start            确保 DSH 已启动（没有托盘时会先拉起托盘）
DeepSeekHarnessTray.exe --stop             停止 DSH（托盘在跑则交给托盘处理）
DeepSeekHarnessTray.exe --restart          重启 DSH
DeepSeekHarnessTray.exe --open             打开页面
DeepSeekHarnessTray.exe --quit             退出托盘（DSH 保持运行）
```

通用选项：

- `--data <目录>`：数据目录（默认 `%LOCALAPPDATA%\DeepSeekHarnessTray`）
- `--port <端口>`：临时覆盖配置里的端口
- `--silent`：不弹气泡、不自动打开浏览器（测试/自动化用）
- `--out <文件>`：把本次输出另存一份

`--status` 输出示例：

```
state=running
managed=true
pid=12345
url=http://127.0.0.1:3080/
authUrl=http://127.0.0.1:3080/?token=...
source=status-file
trayRunning=true
```

`state` 取值：`running` / `external` / `starting` / `stopping` / `stopped` / `error` / `conflict` / `port-conflict`。

### 排障用的环境变量

- `DSHTRAY_DEBUG=1`：`launcher.log` 里记录更细的状态迁移（UI 刷新、气泡、启停步骤），排查问题用。
- `DSHTRAY_INSTANCE=test1`：给启动器一个独立的互斥体和消息窗口，便于和正在运行的托盘并行调试；对应的命令行子命令也要带同样的值。

---

## 版本

- **1.1.0**：退出托盘不再停止 DSH，DSH 改为将输出直接写入 `dsh-web.log`，托盘退出后仍在后台运行并可被下次启动自动接管。
- **1.0.1**：修复点击「停止 DSH / 重启 DSH」后启动器进程崩溃退出的问题（内部状态文件清理方法误写成自递归，触发栈溢出）；新增 `--restart`。
- **1.0.0**：首个版本。

---

## 从源码重新构建

需要 Windows 自带的 .NET Framework 4.x（内含 `csc.exe`），无需 Visual Studio / .NET SDK：

```bat
build.cmd
```

或：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物：`dist\DeepSeekHarnessTray.exe`（约 95 KB，单文件，无额外运行时依赖）。

源码结构（`src\`）：

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 入口、参数解析、单实例互斥体、命令行分发 |
| `ServerController.cs` | 进程生命周期、输出解析、健康探测、状态机 |
| `Resolver.cs` | 查找 node.exe 和 @deepseek-ai/dsh（选最高版本） |
| `Probe.cs` | HTTP/TCP 探测、端口占用者 PID |
| `TrayContext.cs` | 托盘图标、菜单、气泡、开机自启 |
| `Icons.cs` | 按状态绘制托盘图标 |
| `Config.cs` / `StateFile.cs` / `Logger.cs` | 配置、状态文件、日志 |
| `Cli.cs` | 命令行子命令 |
| `Native.cs` / `HiddenWindow.cs` | Win32 消息与隐藏窗口（单实例通信） |

`tools\make-icon.ps1` 生成 exe 图标（多尺寸 PNG 压缩 ICO）。

---

## Git 仓库与发布

项目按 git 仓库组织：仓库只保留源码、脚本和工作流，构建产物（`dist/`、`build/`、`.release/`）都在 `.gitignore` 里。

### 第一次托管到 GitHub

```bash
git init -b main
git add .
git commit -m "Initial commit"
gh repo create dsh-tray --public --source . --remote origin --push
```

需要安装 [GitHub CLI](https://cli.github.com/) 并已 `gh auth login`；私有仓库把 `--public` 换成 `--private`。

首次推送时本地 tag 不会自动上传，执行 `git push origin v1.1.0` 或 `.\release.cmd 1.1.0 -Push` 即可触发第一个 Release。

### 改版本号

```powershell
.\bump-version.cmd 1.2.0
git add -A
git commit -m "release: v1.2.0"
```

脚本会同时修改三处版本：`src/AppPaths.cs`、`src/Properties.cs`、`src/app.manifest`。

### 发布（推荐：GitHub Actions 自动出 exe）

```powershell
.\release.cmd 1.2.0 -Push
```

脚本会构建、打包到 `.release\v1.2.0\`、创建本地 tag `v1.2.0`，然后推送分支和 tag。tag 推送会触发 `.github/workflows/release.yml`：

1. 在 `windows-latest` 上运行 `build.ps1`；
2. 校验 exe 的 `FileVersion` 与 tag 一致；
3. 把 `DeepSeekHarnessTray.exe` 和 `.sha256` 上传到 GitHub Release（自动生成 release notes）。

等价的手动命令：

```bash
git tag -a v1.2.0 -m "v1.2.0"
git push origin main
git push origin v1.2.0
```

### 发布（本地手动创建 Release）

不想等 Actions 时：

```powershell
.\release.cmd 1.2.0 -Manual
```

会推送当前分支，然后用 `gh release create` 直接创建 Release 并上传 exe 和校验文件（不推 tag，避免再触发 Actions）。

### 只本地打包

```powershell
.\release.cmd 1.2.0
```

只构建、打包、创建本地 tag，不推送。产物在 `.release\v1.2.0\`。

### 工作流

| 文件 | 触发 | 作用 |
| --- | --- | --- |
| `.github/workflows/build.yml` | push main / PR / 手动 | 构建并上传 exe artifact |
| `.github/workflows/release.yml` | 推送 `v*` tag | 构建、校验版本、创建 Release |

两个工作流都使用 Windows runner 自带的 .NET Framework 4.x `csc.exe`，不需要 .NET SDK。

---

## 常见问题

**提示“未找到 Node.js 或 @deepseek-ai/dsh”**
先在命令行成功跑一次 `npx @deepseek-ai/dsh web`，或者在 `config.ini` 里写死 `nodePath` 和 `dshEntry`，然后重启托盘。

**打开页面看到 `dsh web authentication required`**
说明这是命令行启动的外部实例，地址里没有本次进程的 token。用菜单「重启 DSH」让托盘接管；或直接从你启动 DSH 的终端里打开它打印的地址。

**端口 3080 被占用**
如果占用者是 DSH，会直接接管；如果是别的程序，会自动换随机端口。想固定端口就改 `config.ini` 的 `port`。

**托盘图标找不到**
Windows 11 默认把新图标放进托盘溢出区（任务栏的 `^`），可以拖到任务栏常驻。首次启动也会弹一个气泡提示。

**杀毒软件 / SmartScreen 提示**
exe 是本地编译、未签名的程序。如果被拦截，选择“更多信息 → 仍要运行”。不放心可以直接用 `build.cmd` 自己编译。

**停止后端口还显示占用几秒**
正常，`TIME_WAIT` 会在几秒内释放。

**日志里出现 `DSH process exited code=1`**
主动停止时 `taskkill` 的退出码就是 1，属于正常；如果是异常退出，`launcher.log` 会带上 `dsh-web.log` 的最后几行原因。

---

## 卸载

1. 托盘菜单 → 退出（DSH 会继续运行，可再执行 `--stop` 停止）。
2. 删除安装目录（默认 `%LOCALAPPDATA%\Programs\DeepSeekHarnessTray`）和桌面/开始菜单快捷方式。
3. 删除数据目录 `%LOCALAPPDATA%\DeepSeekHarnessTray`（配置和日志）。
4. 如开启过开机自启，先在菜单里取消，或删除注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `DeepSeekHarnessTray`。
