# VibeMouse AI Assistant Deployment & Adaptation Guide

This guide is for AI assistants (and engineers using AI assistants) to deploy VibeMouse on a new machine or adapt it to a new environment safely.

## 1) Project Goal and Non-Negotiable Behavior

VibeMouse is a side-button voice workflow tool.

Required behavior:
- Front side button: start/stop recording
- Rear side button when idle: send Enter
- Rear side button while recording: stop recording and dispatch transcript to OpenClaw
- Fallbacks must preserve user output (never silently lose text)

Do not break this state machine while adapting platforms.

## 2) Core Architecture Map

Key modules:
- `vibemouse/cli/main.py`: CLI entry (`run`, `agent run`, `listener run`, `doctor`, `deploy`)
- `vibemouse/core/app.py`: runtime orchestration + state machine + worker lifecycle
- `vibemouse/listener/mouse_listener.py`: side-button capture + gesture path
- `vibemouse/core/audio.py`: microphone recording
- `vibemouse/core/transcriber.py`: ASR backend selection/transcription
- `vibemouse/core/output.py`: text output routing + OpenClaw dispatch + fallback
- `vibemouse/platform/system_integration.py`: platform adapter boundary
- `vibemouse/ops/doctor.py`: environment and runtime diagnostics
- `vibemouse/ops/deploy.py`: deploy logic (Linux systemd, Windows startup, macOS LaunchAgent)
- `vibemouse/config/schema.py`: env config contract
- `vibemouse/ipc/`: IPC server/client for child-process listener mode
- `vibemouse/bindings/`: action binding definitions and resolver

## 3) Platform Adapter (Most Important)

`vibemouse/platform/system_integration.py` contains three fully-implemented platform classes:

- `HyprlandSystemIntegration` — Hyprland/Wayland (Linux)
- `WindowsSystemIntegration` — Windows (Win32 API + pynput)
- `MacOSSystemIntegration` — macOS (osascript + CoreGraphics + pynput)
- `NoopSystemIntegration` — fallback for unrecognized environments

`create_system_integration()` auto-selects based on `XDG_CURRENT_DESKTOP` / `HYPRLAND_INSTANCE_SIGNATURE`, then `sys.platform`.

Methods used by runtime:
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

Rule: add platform-specific behavior here first; avoid spreading platform logic across `core/app.py` and `core/output.py`.

## 4) Dependencies and Download Sources

### Required foundations
- Python 3.10+: https://www.python.org/downloads/
- pip: https://pip.pypa.io/en/stable/installation/

### Runtime and audio
- PortAudio: http://www.portaudio.com/download.html
- libsndfile: https://github.com/libsndfile/libsndfile
- `sounddevice`: https://pypi.org/project/sounddevice/
- `soundfile`: https://pypi.org/project/soundfile/

### Input and desktop integration
- `pynput`: https://pypi.org/project/pynput/
- `evdev` (Linux only): https://python-evdev.readthedocs.io/en/latest/
- PyGObject / AT-SPI (Linux only): https://pygobject.gnome.org/

### ASR and model stack
- FunASR ONNX: https://pypi.org/project/funasr-onnx/
- ONNX Runtime: https://pypi.org/project/onnxruntime/
- ModelScope (model auto-download): https://pypi.org/project/modelscope/
- Optional PyTorch backend: https://pypi.org/project/funasr/
- Optional Intel NPU: https://pypi.org/project/openvino/

### OpenClaw integration target
- OpenClaw repo: https://github.com/openclaw/openclaw

Pinned Python dependencies are defined in `pyproject.toml`.

## 5) Deployment Procedure (Assistant-Executable)

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

Grant Accessibility permission if doctor reports it missing.

### Linux (fastest path, recommended)

```bash
bash scripts/auto-deploy.sh --preset stable
```

Preset choices: `stable`, `fast`, `low-resource`.

Direct alternative:

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -U pip
pip install -e .
vibemouse doctor
vibemouse doctor --fix
vibemouse deploy --preset stable
```

### Validate behavior matrix manually

- idle + rear -> Enter
- recording + rear -> OpenClaw dispatch

### Verify OpenClaw route

```bash
openclaw agent --agent main --message "ping" --json
```

## 6) Service / Startup Deployment

### Linux (systemd user service)

Recommended service file location:
- `~/.config/systemd/user/vibemouse.service`

Lifecycle commands:
```bash
systemctl --user daemon-reload
systemctl --user enable --now vibemouse.service
systemctl --user status vibemouse.service
```

`vibemouse deploy` generates the service and env files automatically.

### Windows (startup entry)

`vibemouse deploy` writes:
- `%APPDATA%\VibeMouse\deploy.env`
- `%APPDATA%\VibeMouse\vibemouse-launch.ps1`
- `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\vibemouse.vbs`

### macOS (LaunchAgent)

`vibemouse deploy` generates and loads a LaunchAgent plist under `~/Library/LaunchAgents/`.

## 7) Environment Contract (Critical Variables)

OpenClaw:
- `VIBEMOUSE_OPENCLAW_COMMAND`
- `VIBEMOUSE_OPENCLAW_AGENT`
- `VIBEMOUSE_OPENCLAW_TIMEOUT_S`
- `VIBEMOUSE_OPENCLAW_RETRIES`

Buttons/state:
- `VIBEMOUSE_FRONT_BUTTON`
- `VIBEMOUSE_REAR_BUTTON`
- `VIBEMOUSE_ENTER_MODE`

ASR performance:
- `VIBEMOUSE_BACKEND`
- `VIBEMOUSE_DEVICE`
- `VIBEMOUSE_PREWARM_ON_START`

Gesture path:
- `VIBEMOUSE_GESTURES_ENABLED`
- `VIBEMOUSE_GESTURE_TRIGGER_BUTTON`
- `VIBEMOUSE_GESTURE_THRESHOLD_PX`
- `VIBEMOUSE_GESTURE_FREEZE_POINTER`

Full list: `vibemouse/config/schema.py`.

## 8) Doctor Checks by Platform

**All platforms:** config validity, OpenClaw, microphone.

**Linux:** input device permissions, Hyprland Return-bind conflict, systemd service state.

**Windows:** pynput hook availability, startup entry, background process.

**macOS:** pynput hook availability, Accessibility permissions, LaunchAgent, background process.

Run with `--fix` to apply safe auto-remediations.

## 9) Adaptation Checklist (Adding a New Platform)

1. Add platform class in `vibemouse/platform/system_integration.py`.
2. Implement shortcut send + active window + focus probe with native APIs.
3. Define terminal detection hints and paste shortcut strategy.
4. Keep fallback chain intact in `vibemouse/core/output.py`.
5. Verify rear-button state machine in `vibemouse/core/app.py` unchanged.
6. Add platform doctor checks in `vibemouse/ops/doctor.py`.
7. Add deploy support in `vibemouse/ops/deploy.py`.
8. Add tests in:
   - `tests/test_system_integration.py`
   - `tests/test_output.py`
   - `tests/test_app.py`
9. Run full verification:
```bash
python -m compileall vibemouse
python -m pytest tests/
vibemouse doctor
```

## 10) Regression Gates (Must Pass Before Merge)

- No change to front/rear state semantics
- OpenClaw dispatch keeps fallback path
- Doctor command still reports useful failures/warnings
- Existing tests pass; new platform tests added
- No destructive change to Linux Hyprland path

## 11) Prompt Template for AI Assistants

Use this prompt when asking an AI assistant to deploy or adapt VibeMouse:

```text
You are deploying/adapting VibeMouse to <TARGET_PLATFORM>.

Constraints:
1) Preserve button state machine:
   - front: start/stop recording
   - rear idle: Enter
   - rear recording: OpenClaw dispatch
2) Implement platform logic only via vibemouse/platform/system_integration.py first.
3) Preserve fallback behavior (clipboard fallback on OpenClaw spawn failure).
4) Add/adjust tests in test_system_integration.py, test_output.py, test_app.py.
5) Run compileall + full pytest + vibemouse doctor and report results.

Deliver:
- code changes
- test changes
- verification evidence
- known platform-specific limitations
```

This keeps adaptation focused, testable, and safe for daily usage.
