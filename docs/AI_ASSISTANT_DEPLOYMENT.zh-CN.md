# VibeMouse AI 助手部署与平台适配指南

本指南面向 AI 助手（以及使用 AI 助手的开发者），用于在新机器部署 VibeMouse，或安全地做新平台适配。

## 1）项目目标与不可破坏行为

VibeMouse 是"鼠标侧键语音工作流"工具。

必须保持的行为：
- 前侧键：开始/结束录音
- 空闲态后侧键：发送 Enter
- 录音态后侧键：停止录音并将转写发送到 OpenClaw
- 任何失败都必须有可见回退，不能静默丢字

做适配时，禁止破坏上述状态机。

## 2）核心架构地图

关键模块：
- `vibemouse/cli/main.py`：CLI 入口（`run`、`agent run`、`listener run`、`doctor`、`deploy`）
- `vibemouse/core/app.py`：主状态机、线程编排、输出路由
- `vibemouse/listener/mouse_listener.py`：侧键监听与手势路径
- `vibemouse/core/audio.py`：录音
- `vibemouse/core/transcriber.py`：ASR 后端与识别
- `vibemouse/core/output.py`：输入/剪贴板/OpenClaw 路由与回退
- `vibemouse/platform/system_integration.py`：平台适配边界
- `vibemouse/ops/doctor.py`：部署与运行自检
- `vibemouse/ops/deploy.py`：部署逻辑（Linux systemd、Windows 启动项、macOS LaunchAgent）
- `vibemouse/config/schema.py`：环境变量配置契约
- `vibemouse/ipc/`：子进程监听器模式的 IPC 通信
- `vibemouse/bindings/`：动作绑定定义与解析器

## 3）平台适配边界（最重要）

`vibemouse/platform/system_integration.py` 已包含三个完整实现的平台类：

- `HyprlandSystemIntegration` — Hyprland/Wayland（Linux）
- `WindowsSystemIntegration` — Windows（Win32 API + pynput）
- `MacOSSystemIntegration` — macOS（osascript + CoreGraphics + pynput）
- `NoopSystemIntegration` — 未识别环境的兜底

`create_system_integration()` 按 `XDG_CURRENT_DESKTOP` / `HYPRLAND_INSTANCE_SIGNATURE`，再按 `sys.platform` 自动选择。

运行时依赖的方法：
- `is_hyprland`
- `send_shortcut(mod, key)`
- `active_window()`
- `cursor_position()`
- `move_cursor(x, y)`
- `switch_workspace(direction)`
- `is_text_input_focused()`
- `send_enter_via_accessibility()`
- `is_terminal_window_active()`
- `paste_shortcuts(terminal_active)`

原则：平台特化逻辑优先放在这里，不要散落到 `core/app.py` 和 `core/output.py`。

## 4）依赖项与下载地址

### 基础环境
- Python 3.10+：https://www.python.org/downloads/
- pip 安装文档：https://pip.pypa.io/en/stable/installation/

### 音频链路
- PortAudio：http://www.portaudio.com/download.html
- libsndfile：https://github.com/libsndfile/libsndfile
- `sounddevice`：https://pypi.org/project/sounddevice/
- `soundfile`：https://pypi.org/project/soundfile/

### 输入与桌面集成
- `pynput`：https://pypi.org/project/pynput/
- `evdev`（仅 Linux）：https://python-evdev.readthedocs.io/en/latest/
- PyGObject / AT-SPI（仅 Linux）：https://pygobject.gnome.org/

### 语音识别与模型栈
- FunASR ONNX：https://pypi.org/project/funasr-onnx/
- ONNX Runtime：https://pypi.org/project/onnxruntime/
- ModelScope（模型自动下载）：https://pypi.org/project/modelscope/
- 可选 PyTorch 后端：https://pypi.org/project/funasr/
- 可选 Intel NPU：https://pypi.org/project/openvino/

### OpenClaw 目标
- OpenClaw 仓库：https://github.com/openclaw/openclaw

Python 依赖版本以 `pyproject.toml` 为准。

## 5）部署步骤（可直接让 AI 助手执行）

### Windows

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -U pip
python -m pip install -e .
vibemouse doctor
vibemouse deploy
vibemouse
```

### macOS

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -U pip
pip install -e .
vibemouse doctor
vibemouse deploy
vibemouse
```

doctor 报辅助功能权限缺失时，打开「系统偏好设置 → 隐私与安全性 → 辅助功能」授权。

### Linux（最快，推荐）

```bash
bash scripts/auto-deploy.sh --preset stable
```

预设可选：`stable`、`fast`、`low-resource`。

也可以直接用 deploy 子命令：

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -U pip
pip install -e .
vibemouse doctor
vibemouse doctor --fix
vibemouse deploy --preset stable
```

### 手工验证状态矩阵
- 空闲态后侧键 -> Enter
- 录音态后侧键 -> OpenClaw 路由

### 验证 OpenClaw
```bash
openclaw agent --agent main --message "ping" --json
```

## 6）服务 / 开机启动部署

### Linux（systemd user service）

推荐 service 文件路径：
- `~/.config/systemd/user/vibemouse.service`

基础命令：
```bash
systemctl --user daemon-reload
systemctl --user enable --now vibemouse.service
systemctl --user status vibemouse.service
```

`vibemouse deploy` 会自动生成 service 和 env 文件。

### Windows（启动项）

`vibemouse deploy` 写入：
- `%APPDATA%\VibeMouse\deploy.env`
- `%APPDATA%\VibeMouse\vibemouse-launch.ps1`
- `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\vibemouse.vbs`

### macOS（LaunchAgent）

`vibemouse deploy` 自动生成并加载 `~/Library/LaunchAgents/` 下的 LaunchAgent plist。

## 7）环境变量契约（关键）

OpenClaw：
- `VIBEMOUSE_OPENCLAW_COMMAND`
- `VIBEMOUSE_OPENCLAW_AGENT`
- `VIBEMOUSE_OPENCLAW_TIMEOUT_S`
- `VIBEMOUSE_OPENCLAW_RETRIES`

按钮与状态：
- `VIBEMOUSE_FRONT_BUTTON`
- `VIBEMOUSE_REAR_BUTTON`
- `VIBEMOUSE_ENTER_MODE`

识别性能：
- `VIBEMOUSE_BACKEND`
- `VIBEMOUSE_DEVICE`
- `VIBEMOUSE_PREWARM_ON_START`

手势：
- `VIBEMOUSE_GESTURES_ENABLED`
- `VIBEMOUSE_GESTURE_TRIGGER_BUTTON`
- `VIBEMOUSE_GESTURE_THRESHOLD_PX`
- `VIBEMOUSE_GESTURE_FREEZE_POINTER`

完整列表以 `vibemouse/config/schema.py` 为准。

## 8）分平台的 Doctor 检查项

**全平台：** 配置有效性、OpenClaw、麦克风。

**Linux：** 输入设备权限、Hyprland Return 绑定冲突、systemd 服务状态。

**Windows：** pynput 钩子可用性、启动项存在性、后台进程状态。

**macOS：** pynput 钩子可用性、辅助功能权限、LaunchAgent 存在性、后台进程状态。

加 `--fix` 参数可执行安全自动修复后重检。

## 9）新平台适配检查单

1. 在 `vibemouse/platform/system_integration.py` 增加平台类。
2. 用本地 API 实现快捷键发送、活动窗口检测、焦点探测。
3. 定义终端识别与粘贴策略。
4. 保留 `vibemouse/core/output.py` 的回退链路。
5. 保证 `vibemouse/core/app.py` 按键状态机不变。
6. 在 `vibemouse/ops/doctor.py` 补充平台 doctor 检查。
7. 在 `vibemouse/ops/deploy.py` 补充启动部署支持。
8. 补充测试：
   - `tests/test_system_integration.py`
   - `tests/test_output.py`
   - `tests/test_app.py`
9. 跑完整验证：
```bash
python -m compileall vibemouse
python -m pytest tests/
vibemouse doctor
```

## 10）合并前回归门槛

- 前/后侧键状态语义不变
- OpenClaw 路由仍有失败回退
- doctor 输出对故障可读、可定位
- 全量测试通过，并补充平台测试
- Linux Hyprland 主路径不能退化

## 11）给 AI 助手的提示模板

可以直接把下面提示词交给 AI 助手：

```text
你要把 VibeMouse 部署/适配到 <目标平台>。

约束：
1）必须保持按钮状态机：
   - 前侧键：开始/结束录音
   - 后侧键空闲态：Enter
   - 后侧键录音态：OpenClaw 路由
2）平台逻辑优先放在 vibemouse/platform/system_integration.py，不要散落到其它模块。
3）必须保留失败回退（OpenClaw 启动失败回退到剪贴板）。
4）更新 test_system_integration.py、test_output.py、test_app.py。
5）执行 compileall + 全量 pytest + vibemouse doctor，并报告结果。

交付：
- 代码改动
- 测试改动
- 验证证据
- 平台限制说明
```

这能保证适配过程可控、可测、可长期维护。
