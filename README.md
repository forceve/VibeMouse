# VibeMouse

Mouse-side-button voice input for VibeCoding.

中文文档：[`README.zh-CN.md`](./README.zh-CN.md)

AI adaptation guides:
- English: [`docs/AI_ASSISTANT_DEPLOYMENT.md`](./docs/AI_ASSISTANT_DEPLOYMENT.md)
- 中文：[`docs/AI_ASSISTANT_DEPLOYMENT.zh-CN.md`](./docs/AI_ASSISTANT_DEPLOYMENT.zh-CN.md)
- AI debug runbook: [`docs/AI_DEBUG_RUNBOOK.md`](./docs/AI_DEBUG_RUNBOOK.md)
- IPC integration: [`docs/IPC.md`](./docs/IPC.md)
- IPC 集成说明: [`docs/IPC.zh-CN.md`](./docs/IPC.zh-CN.md)

## What This Project Does

VibeMouse binds your coding speech workflow to mouse side buttons:
- Front side button: start/stop recording
- Rear side button while idle: send Enter
- Rear side button while recording: stop recording and route transcript to OpenClaw

Core goals are low friction, stable daily use, and graceful fallback when any subsystem fails.

## Runtime Architecture (Core)

The runtime is event-driven and split by responsibility:

1. `vibemouse/cli/main.py`
   - CLI entry (`run`, `agent run`, `listener run`, `doctor`, `deploy`); default agent mode is child-listener supervision
2. `vibemouse/core/app.py`
   - Orchestrates button events, recording state, transcription workers, and final output routing
3. `vibemouse/listener/mouse_listener.py`
   - Captures side buttons and gestures (`evdev` on Linux, `pynput` on Windows/macOS)
4. `vibemouse/core/audio.py`
   - Records audio to temp WAV
5. `vibemouse/core/transcriber.py`
   - SenseVoice backend selection and transcription
6. `vibemouse/core/output.py`
   - Text typing / clipboard / OpenClaw dispatch, with fallback and reason tracking
7. `vibemouse/platform/system_integration.py`
   - Platform adapters: `HyprlandSystemIntegration`, `WindowsSystemIntegration`, `MacOSSystemIntegration`
8. `vibemouse/ops/doctor.py`
   - Built-in diagnostics for env, OpenClaw, input permissions, and known conflicts
9. `vibemouse/ipc/`
   - IPC server/client for child-process listener mode (`vibemouse agent run --listener=child`)
10. `vibemouse/bindings/`
    - Action binding definitions and resolver

## Quick Start

### Windows

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -U pip
python -m pip install -e .
```

Run diagnostics first:
```powershell
vibemouse doctor
```

Start:
```powershell
vibemouse
```

Default runtime mode is `listener=child`. Use `vibemouse agent run --listener=inline`
only as a compatibility fallback while debugging listener-process issues.

Set up auto-start on login:
```powershell
vibemouse deploy
```

Windows deploy writes:
- `%APPDATA%\VibeMouse\deploy.env`
- `%APPDATA%\VibeMouse\vibemouse-launch.ps1`
- `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\vibemouse.vbs`

### macOS

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -U pip
pip install -e .
```

Grant Accessibility permission when prompted (required for text input focus detection and shortcut sending).

Run diagnostics:
```bash
vibemouse doctor
```

Start:
```bash
vibemouse
```

Default runtime mode is `listener=child`. Use `vibemouse agent run --listener=inline`
only as a compatibility fallback while debugging listener-process issues.

Set up LaunchAgent:
```bash
vibemouse deploy
```

### Linux (Ubuntu / Debian)

Install system packages:
```bash
sudo apt update
sudo apt install -y python3-gi gir1.2-atspi-2.0 portaudio19-dev libsndfile1
```

### Linux (Arch)

```bash
sudo pacman -Syu --needed python python-pip python-gobject portaudio libsndfile
```

### Linux install and run

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -U pip
pip install -e .
vibemouse
```

This starts the agent in the default `listener=child` mode.

Default install is ONNX-first for smaller deployment footprint.

- Optional PyTorch backend (GPU/advanced fallback): `pip install -e ".[pt]"`
- Optional Intel NPU dependencies: `pip install -e ".[npu]"`

### One-command auto deploy for Linux (recommended)

```bash
bash scripts/auto-deploy.sh --preset stable
```

This command bootstraps `.venv`, installs VibeMouse, generates service/env files,
enables `systemd --user` service, and runs `vibemouse doctor`.

Available presets:
- `stable`: balanced daily-driver defaults
- `fast`: lower debounce + higher OpenClaw retries
- `low-resource`: lower background footprint defaults

Examples:

```bash
bash scripts/auto-deploy.sh --preset stable
bash scripts/auto-deploy.sh --preset low-resource
bash scripts/auto-deploy.sh --preset stable --openclaw-agent ops
```

## Default Mapping and State Logic

- `VIBEMOUSE_FRONT_BUTTON` default: `x1`
- `VIBEMOUSE_REAR_BUTTON` default: `x2`

State matrix:
- Idle + rear press -> Enter (`VIBEMOUSE_ENTER_MODE`)
- Recording + rear press -> stop recording + OpenClaw dispatch

If your hardware labels are reversed:

```bash
export VIBEMOUSE_FRONT_BUTTON=x2
export VIBEMOUSE_REAR_BUTTON=x1
```

## OpenClaw Integration (Core)

OpenClaw route is explicit and configurable:
- `VIBEMOUSE_OPENCLAW_COMMAND` (default `openclaw`)
- `VIBEMOUSE_OPENCLAW_AGENT` (default `main`)
- `VIBEMOUSE_OPENCLAW_TIMEOUT_S` (default `20.0`)
- `VIBEMOUSE_OPENCLAW_RETRIES` (default `0`)

Dispatch behavior:
- Fast fire-and-forget spawn to avoid blocking UI interaction
- Route result includes reason (`dispatched`, `dispatched_after_retry_*`, `spawn_error:*`, etc.)
- Clipboard fallback if command is invalid or spawn fails

Deployment tip: if you run your own local assistant setup, set
`VIBEMOUSE_OPENCLAW_AGENT` to your own assistant ID.

## Built-in Doctor

Run diagnostics:

```bash
vibemouse doctor
```

Apply safe auto-fixes first, then re-check:

```bash
vibemouse doctor --fix
```

Checks by platform:

**All platforms:**
- Config load validity
- OpenClaw command resolution + agent existence
- Microphone input availability

**Linux:**
- Input device permissions / side-button capability
- Hyprland rear-button Return bind conflicts
- `systemctl --user` service activity

**Windows:**
- pynput input hook availability
- Startup entry presence
- Background process status

**macOS:**
- pynput input hook availability
- Accessibility permissions
- LaunchAgent presence
- Background process status

Exit code is non-zero when any `FAIL` check exists.

## Deploy Command

The deploy command is scriptable and can be used directly:

```bash
vibemouse deploy --preset stable
```

Useful flags:
- `--preset stable|fast|low-resource`
- `--openclaw-command "openclaw --profile prod"`
- `--openclaw-agent main`
- `--openclaw-retries 2`
- `--log-file ~/.local/state/vibemouse/service.log`
- `--skip-systemctl` (Linux)
- `--dry-run`

Persistent debug logs (Linux, recommended):

```bash
tail -f ~/.local/state/vibemouse/service.log
```

## Frequently Used Variables

| Variable | Default | Purpose |
|---|---|---|
| `VIBEMOUSE_ENTER_MODE` | `enter` | Rear-button submit mode (`enter`, `ctrl_enter`, `shift_enter`, `none`) |
| `VIBEMOUSE_AUTO_PASTE` | `false` | Auto paste when route falls back to clipboard |
| `VIBEMOUSE_GESTURES_ENABLED` | `false` | Enable gesture recognition |
| `VIBEMOUSE_GESTURE_TRIGGER_BUTTON` | `rear` | Gesture trigger (`front`, `rear`, `right`) |
| `VIBEMOUSE_GESTURE_THRESHOLD_PX` | `120` | Gesture movement threshold |
| `VIBEMOUSE_GESTURE_FREEZE_POINTER` | `true` | Freeze pointer during gesture capture |
| `VIBEMOUSE_PREWARM_ON_START` | `true` | Preload ASR on startup to reduce first-use latency |
| `VIBEMOUSE_PREWARM_DELAY_S` | `0.0` | Delay ASR prewarm after startup to improve initial responsiveness |
| `VIBEMOUSE_STATUS_FILE` | `$XDG_RUNTIME_DIR/vibemouse-status.json` | Runtime status for bars/widgets |

Full configuration source of truth: `vibemouse/config/schema.py`.

## Troubleshooting Shortlist

### Postmortem: "Everything stopped working" (record/gesture/enter)

When users report that recording, right-button gestures, and Enter all fail together,
the most common root cause is **mouse side-button event mismatch**, not a dead service.

Typical failure pattern:
- Service is `active`, but button actions never trigger.
- Hyprland workspace commands still return `ok` when run manually.
- User perception: "all features are broken".

Real root causes we hit:
1. Side-button codes were only matched as `BTN_SIDE`/`BTN_EXTRA`.
2. Some mice emit `BTN_BACK`/`BTN_FORWARD` aliases instead.
3. Runtime env had action mappings, but listener never recognized raw events.

Current fix in code:
- `x1` accepts `{BTN_SIDE, BTN_BACK}`
- `x2` accepts `{BTN_EXTRA, BTN_FORWARD}`

Fast verification order (recommended):
1. `systemctl --user is-active vibemouse.service`
2. `hyprctl dispatch workspace e-1` and `hyprctl dispatch workspace e+1`
3. `vibemouse doctor`
4. Confirm runtime env from `/proc/<MainPID>/environ`:
   - `VIBEMOUSE_GESTURE_TRIGGER_BUTTON`
   - `VIBEMOUSE_GESTURE_LEFT_ACTION`
   - `VIBEMOUSE_GESTURE_RIGHT_ACTION`
   - `VIBEMOUSE_FRONT_BUTTON` / `VIBEMOUSE_REAR_BUTTON`

If (1)-(3) pass but buttons still do nothing, debug listener code-path first.

### Rear button still sends Enter while recording (Linux/Hyprland)

Check Hyprland-level hard bind conflict in
`~/.config/hypr/UserConfigs/UserKeybinds.conf` and remove lines like:

```ini
bind = , mouse:275, sendshortcut, , Return, activewindow
bind = , mouse:276, sendshortcut, , Return, activewindow
```

Then reload:

```bash
hyprctl reload config-only
```

### OpenClaw route not working

```bash
openclaw agent --agent main --message "ping" --json
vibemouse doctor
```

### Side button not detected on Linux

```bash
sudo usermod -aG input $USER
# relogin required
```

### macOS: shortcut sending or focus detection not working

Open **System Preferences → Privacy & Security → Accessibility** and ensure the terminal (or VibeMouse process) is allowed.

## For AI Assistants and Platform Adapters

- [`docs/AI_ASSISTANT_DEPLOYMENT.md`](./docs/AI_ASSISTANT_DEPLOYMENT.md)
- [`docs/AI_ASSISTANT_DEPLOYMENT.zh-CN.md`](./docs/AI_ASSISTANT_DEPLOYMENT.zh-CN.md)
- [`docs/AI_DEBUG_RUNBOOK.md`](./docs/AI_DEBUG_RUNBOOK.md)

Contains architecture contracts, dependency download links, adaptation workflow,
and a prompt template for autonomous deployment.

## License

Source code is licensed under Apache-2.0. See `LICENSE`.

Third-party and model asset notices: `THIRD_PARTY_NOTICES.md`.
