from __future__ import annotations

import argparse
import os
import shlex
import shutil
import subprocess
import sys
from html import escape
from pathlib import Path
from typing import cast

from vibemouse.ops.doctor import run_doctor


_PRESET_OVERRIDES: dict[str, dict[str, str]] = {
    "stable": {
        "VIBEMOUSE_AUTO_PASTE": "true",
        "VIBEMOUSE_BUTTON_DEBOUNCE_MS": "220",
        "VIBEMOUSE_PREWARM_ON_START": "true",
        "VIBEMOUSE_OPENCLAW_RETRIES": "1",
    },
    "fast": {
        "VIBEMOUSE_AUTO_PASTE": "true",
        "VIBEMOUSE_BUTTON_DEBOUNCE_MS": "120",
        "VIBEMOUSE_PREWARM_ON_START": "true",
        "VIBEMOUSE_OPENCLAW_RETRIES": "2",
    },
    "low-resource": {
        "VIBEMOUSE_AUTO_PASTE": "false",
        "VIBEMOUSE_BUTTON_DEBOUNCE_MS": "250",
        "VIBEMOUSE_PREWARM_ON_START": "false",
        "VIBEMOUSE_OPENCLAW_RETRIES": "0",
    },
}


def configure_deploy_parser(parser: argparse.ArgumentParser) -> None:
    _ = parser.add_argument(
        "--preset",
        choices=sorted(_PRESET_OVERRIDES.keys()),
        default="stable",
        help="deployment preset profile",
    )
    _ = parser.add_argument(
        "--env-file",
        default=str(_default_env_file()),
        help="path to generated environment file",
    )
    _ = parser.add_argument(
        "--log-file",
        default=str(_default_log_file()),
        help="path to persistent log file",
    )
    _ = parser.add_argument(
        "--openclaw-command",
        default=shutil.which("openclaw") or "openclaw",
        help="OpenClaw command prefix",
    )
    _ = parser.add_argument(
        "--openclaw-agent",
        default="main",
        help="OpenClaw agent id used for rear-button routing",
    )
    _ = parser.add_argument(
        "--openclaw-retries",
        type=int,
        default=None,
        help="override retries for OpenClaw spawn failures",
    )
    _ = parser.add_argument(
        "--exec-start",
        default=None,
        help="override ExecStart command",
    )
    _ = parser.add_argument(
        "--dry-run",
        action="store_true",
        help="print plan without writing files",
    )

    if _is_windows():
        _ = parser.add_argument(
            "--launcher-file",
            default=str(_default_windows_launcher_file()),
            help="path to generated PowerShell launcher",
        )
        _ = parser.add_argument(
            "--startup-file",
            default=str(_default_windows_startup_file()),
            help="path to generated Startup-folder entry",
        )
        _ = parser.add_argument(
            "--skip-register",
            action="store_true",
            help="skip creating the Startup-folder entry",
        )
    elif _is_macos():
        _ = parser.add_argument(
            "--launch-agent-file",
            default=str(_default_macos_launch_agent_file()),
            help="path to generated launchd plist",
        )
        _ = parser.add_argument(
            "--skip-launchctl",
            action="store_true",
            help="skip launchctl bootstrap/kickstart operations",
        )
    else:
        _ = parser.add_argument(
            "--service-file",
            default=str(
                Path.home() / ".config" / "systemd" / "user" / "vibemouse.service"
            ),
            help="path to generated systemd user service file",
        )
        _ = parser.add_argument(
            "--skip-systemctl",
            action="store_true",
            help="skip systemctl enable/restart operations",
        )


def run_deploy(args: argparse.Namespace) -> int:
    preset = str(getattr(args, "preset", "stable"))
    if preset not in _PRESET_OVERRIDES:
        print(f"Unknown preset: {preset}")
        return 1

    openclaw_command = str(getattr(args, "openclaw_command", "openclaw")).strip()
    if not openclaw_command:
        print("--openclaw-command must not be empty")
        return 1

    openclaw_agent = str(getattr(args, "openclaw_agent", "main")).strip() or "main"

    retries_override = cast(int | None, getattr(args, "openclaw_retries", None))

    if retries_override is not None and retries_override < 0:
        print("--openclaw-retries must be non-negative")
        return 1

    env_path = Path(str(getattr(args, "env_file", ""))).expanduser()
    log_path = Path(str(getattr(args, "log_file", ""))).expanduser()
    exec_start = _resolve_exec_start(str(getattr(args, "exec_start", "") or ""))

    env_map = build_deploy_env(
        preset=preset,
        openclaw_command=openclaw_command,
        openclaw_agent=openclaw_agent,
        openclaw_retries=retries_override,
    )
    env_content = render_env_file(env_map)

    if _is_windows():
        launcher_path = Path(str(getattr(args, "launcher_file", ""))).expanduser()
        startup_path = Path(str(getattr(args, "startup_file", ""))).expanduser()
        launcher_content = render_windows_launcher(
            env_file=env_path,
            log_file=log_path,
            exec_start=exec_start,
        )
        startup_content = render_windows_startup_file(launcher_file=launcher_path)

        if bool(getattr(args, "dry_run", False)):
            print(f"[DRY-RUN] would write {env_path}")
            print(f"[DRY-RUN] would write {launcher_path}")
            if not bool(getattr(args, "skip_register", False)):
                print(f"[DRY-RUN] would write {startup_path}")
            print(f"[DRY-RUN] preset={preset}")
            print(f"[DRY-RUN] exec_start={exec_start}")
            return 0

        _write_text(env_path, env_content)
        _write_text(launcher_path, launcher_content)
        print(f"Wrote {env_path}")
        print(f"Wrote {launcher_path}")
        if not bool(getattr(args, "skip_register", False)):
            _write_text(startup_path, startup_content)
            print(f"Wrote {startup_path}")

        print("Running doctor checks...")
        return run_doctor()

    if _is_macos():
        launch_agent_path = Path(
            str(getattr(args, "launch_agent_file", ""))
        ).expanduser()
        launch_agent_content = render_macos_launch_agent(
            env_file=env_path,
            log_file=log_path,
            exec_start=exec_start,
        )

        if bool(getattr(args, "dry_run", False)):
            print(f"[DRY-RUN] would write {env_path}")
            print(f"[DRY-RUN] would write {launch_agent_path}")
            print(f"[DRY-RUN] preset={preset}")
            print(f"[DRY-RUN] exec_start={exec_start}")
            return 0

        _write_text(env_path, env_content)
        _write_text(launch_agent_path, launch_agent_content)
        print(f"Wrote {env_path}")
        print(f"Wrote {launch_agent_path}")

        log_path.parent.mkdir(parents=True, exist_ok=True)

        if not bool(getattr(args, "skip_launchctl", False)):
            if not _run_launchctl_bootstrap(launch_agent_path):
                return 1

        print("Running doctor checks...")
        return run_doctor()

    service_path = Path(str(getattr(args, "service_file", ""))).expanduser()
    service_content = render_service_file(
        env_file=env_path,
        log_file=log_path,
        exec_start=exec_start,
    )

    if bool(getattr(args, "dry_run", False)):
        print(f"[DRY-RUN] would write {env_path}")
        print(f"[DRY-RUN] would write {service_path}")
        print(f"[DRY-RUN] preset={preset}")
        print(f"[DRY-RUN] exec_start={exec_start}")
        return 0

    _write_text(env_path, env_content)
    _write_text(service_path, service_content)
    print(f"Wrote {env_path}")
    print(f"Wrote {service_path}")

    if not bool(getattr(args, "skip_systemctl", False)):
        service_name = service_path.name
        if not _run_systemctl(["daemon-reload"]):
            return 1
        if not _run_systemctl(["enable", "--now", service_name]):
            return 1
        if not _run_systemctl(["is-active", service_name]):
            return 1

    print("Running doctor checks...")
    return run_doctor()


def build_deploy_env(
    *,
    preset: str,
    openclaw_command: str,
    openclaw_agent: str,
    openclaw_retries: int | None,
) -> dict[str, str]:
    status_file = (
        _default_windows_status_file()
        if _is_windows()
        else _default_macos_status_file()
        if _is_macos()
        else "%t/vibemouse-status.json"
    )
    base = {
        "VIBEMOUSE_BACKEND": "funasr_onnx",
        "VIBEMOUSE_DEVICE": "cpu",
        "VIBEMOUSE_FALLBACK_CPU": "true",
        "VIBEMOUSE_ENTER_MODE": "enter",
        "VIBEMOUSE_OPENCLAW_COMMAND": openclaw_command,
        "VIBEMOUSE_OPENCLAW_AGENT": openclaw_agent,
        "VIBEMOUSE_OPENCLAW_TIMEOUT_S": "20.0",
        "VIBEMOUSE_STATUS_FILE": status_file,
    }
    base.update(_PRESET_OVERRIDES[preset])
    if openclaw_retries is not None:
        base["VIBEMOUSE_OPENCLAW_RETRIES"] = str(openclaw_retries)
    return base


def render_env_file(env_map: dict[str, str]) -> str:
    lines = [
        "# Generated by `vibemouse deploy`.",
        "# Edit values if needed, then restart VibeMouse.",
    ]
    for key in sorted(env_map.keys()):
        lines.append(f"{key}={_quote_env_value(env_map[key])}")
    lines.append("")
    return "\n".join(lines)


def render_service_file(*, env_file: Path, log_file: Path, exec_start: str) -> str:
    env_file_str = env_file.as_posix()
    log_file_str = log_file.as_posix()
    log_dir = log_file.parent.as_posix()
    lines = [
        "[Unit]",
        "Description=VibeMouse voice input service",
        "After=graphical-session.target",
        "PartOf=graphical-session.target",
        "",
        "[Service]",
        "Type=simple",
        f"EnvironmentFile={env_file_str}",
        f"ExecStartPre=/usr/bin/mkdir -p {log_dir}",
        f"ExecStart={exec_start}",
        f"StandardOutput=append:{log_file_str}",
        f"StandardError=append:{log_file_str}",
        "Restart=on-failure",
        "RestartSec=2",
        "",
        "[Install]",
        "WantedBy=default.target",
        "",
    ]
    return "\n".join(lines)


def render_windows_launcher(*, env_file: Path, log_file: Path, exec_start: str) -> str:
    lines = [
        '$ErrorActionPreference = "Stop"',
        f"$envFile = '{_ps_single_quote(str(env_file))}'",
        f"$logFile = '{_ps_single_quote(str(log_file))}'",
        "$commandLine = @'",
        exec_start,
        "'@.Trim()",
        "",
        "if (Test-Path $envFile) {",
        "  Get-Content $envFile | ForEach-Object {",
        "    $line = $_.Trim()",
        '    if (-not $line -or $line.StartsWith("#")) { return }',
        '    $parts = $line -split "=", 2',
        "    if ($parts.Length -ne 2) { return }",
        "    $name = $parts[0].Trim()",
        "    $value = $parts[1].Trim()",
        '    if ($value.Length -ge 2 -and $value.StartsWith(\'"\') -and $value.EndsWith(\'"\')) {',
        "      $value = $value.Substring(1, $value.Length - 2)",
        '      $value = $value.Replace(\'\\\\\', \'\\\')',
        '      $value = $value.Replace(\'\\"\', \'"\')',
        "    }",
        '    [System.Environment]::SetEnvironmentVariable($name, $value, "Process")',
        "  }",
        "}",
        "",
        "$logDir = Split-Path -Parent $logFile",
        'New-Item -ItemType Directory -Path $logDir -Force | Out-Null',
        '& cmd.exe /d /c "$commandLine >> `"$logFile`" 2>>&1"',
        "exit $LASTEXITCODE",
        "",
    ]
    return "\n".join(lines)


def render_windows_startup_file(*, launcher_file: Path) -> str:
    launcher = str(launcher_file).replace('"', '""')
    command = (
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden '
        + f'-File "{launcher}"'
    )
    return "\n".join(
        [
            'Set shell = CreateObject("WScript.Shell")',
            f'shell.Run "{command.replace(chr(34), chr(34) * 2)}", 0, False',
            "",
        ]
    )


def render_macos_launch_agent(*, env_file: Path, log_file: Path, exec_start: str) -> str:
    command = _macos_exec_wrapper(
        env_file=env_file,
        log_file=log_file,
        exec_start=exec_start,
    )
    label = "io.vibemouse.agent"
    entries = [
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" "
        + "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
        "<plist version=\"1.0\">",
        "<dict>",
        f"  <key>Label</key><string>{label}</string>",
        "  <key>ProgramArguments</key>",
        "  <array>",
        "    <string>/bin/sh</string>",
        "    <string>-lc</string>",
        f"    <string>{escape(command)}</string>",
        "  </array>",
        "  <key>RunAtLoad</key><true/>",
        "  <key>KeepAlive</key><true/>",
        f"  <key>StandardOutPath</key><string>{escape(str(log_file))}</string>",
        f"  <key>StandardErrorPath</key><string>{escape(str(log_file))}</string>",
        "</dict>",
        "</plist>",
        "",
    ]
    return "\n".join(entries)


def _quote_env_value(value: str) -> str:
    escaped = value.replace("\\", "\\\\").replace('"', '\\"')
    return f'"{escaped}"'


def _resolve_exec_start(raw_exec_start: str) -> str:
    cleaned = raw_exec_start.strip()
    if cleaned:
        return cleaned

    vibemouse_bin = shutil.which("vibemouse")
    if vibemouse_bin:
        return f'{_quote_shell_path(vibemouse_bin)} run'

    python_bin = sys.executable
    return f'{_quote_shell_path(python_bin)} -m vibemouse.main run'


def _write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    _ = path.write_text(content, encoding="utf-8")


def _run_systemctl(args: list[str]) -> bool:
    cmd = ["systemctl", "--user", *args]
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            check=False,
            timeout=12.0,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        print(f"Failed to run {' '.join(cmd)}: {error}")
        return False

    if proc.returncode == 0:
        return True

    stderr = proc.stderr.strip()
    if stderr:
        print(f"systemctl {' '.join(args)} failed: {stderr}")
    else:
        print(f"systemctl {' '.join(args)} failed with code {proc.returncode}")
    return False


def _run_launchctl_bootstrap(service_path: Path) -> bool:
    domain = f"gui/{os.getuid()}"
    _ = _run_process(
        ["launchctl", "bootout", domain, str(service_path)],
        timeout=12.0,
    )
    bootstrap = _run_process(
        ["launchctl", "bootstrap", domain, str(service_path)],
        timeout=12.0,
    )
    if bootstrap is None:
        print("Failed to run launchctl bootstrap")
        return False
    if bootstrap.returncode != 0:
        stderr = bootstrap.stderr.strip()
        print(stderr or "launchctl bootstrap failed")
        return False

    kickstart = _run_process(
        ["launchctl", "kickstart", "-k", f"{domain}/io.vibemouse.agent"],
        timeout=12.0,
    )
    if kickstart is None:
        print("Failed to run launchctl kickstart")
        return False
    if kickstart.returncode != 0:
        stderr = kickstart.stderr.strip()
        print(stderr or "launchctl kickstart failed")
        return False
    return True


def validate_openclaw_command(raw: str) -> bool:
    try:
        parts = shlex.split(raw)
    except ValueError:
        return False
    return bool(parts)


def _is_windows() -> bool:
    return sys.platform.startswith("win")


def _is_macos() -> bool:
    return sys.platform == "darwin"


def _default_env_file() -> Path:
    if _is_windows():
        return _windows_roaming_dir() / "VibeMouse" / "deploy.env"
    return Path.home() / ".config" / "vibemouse" / "deploy.env"


def _default_log_file() -> Path:
    if _is_windows():
        return _windows_local_dir() / "VibeMouse" / "service.log"
    if _is_macos():
        return Path.home() / "Library" / "Logs" / "VibeMouse" / "service.log"
    return Path.home() / ".local" / "state" / "vibemouse" / "service.log"


def _default_windows_status_file() -> str:
    return str(_windows_local_dir() / "VibeMouse" / "vibemouse-status.json")


def _default_macos_status_file() -> str:
    return str(
        Path.home()
        / "Library"
        / "Application Support"
        / "VibeMouse"
        / "vibemouse-status.json"
    )


def _default_windows_launcher_file() -> Path:
    return _windows_roaming_dir() / "VibeMouse" / "vibemouse-launch.ps1"


def _default_windows_startup_file() -> Path:
    return _windows_startup_dir() / "vibemouse.vbs"


def _default_macos_launch_agent_file() -> Path:
    return Path.home() / "Library" / "LaunchAgents" / "io.vibemouse.agent.plist"


def _windows_roaming_dir() -> Path:
    appdata = os.getenv("APPDATA")
    if appdata:
        return Path(appdata)
    return Path.home() / "AppData" / "Roaming"


def _windows_local_dir() -> Path:
    localappdata = os.getenv("LOCALAPPDATA")
    if localappdata:
        return Path(localappdata)
    return Path.home() / "AppData" / "Local"


def _windows_startup_dir() -> Path:
    return (
        _windows_roaming_dir()
        / "Microsoft"
        / "Windows"
        / "Start Menu"
        / "Programs"
        / "Startup"
    )


def _quote_shell_path(path: str) -> str:
    if " " in path or "\t" in path:
        return f'"{path}"'
    return path


def _ps_single_quote(value: str) -> str:
    return value.replace("'", "''")


def _macos_exec_wrapper(*, env_file: Path, log_file: Path, exec_start: str) -> str:
    env_file_q = shlex.quote(str(env_file))
    log_file_q = shlex.quote(str(log_file))
    log_dir_q = shlex.quote(str(log_file.parent))
    return (
        f"set -a; [ -f {env_file_q} ] && . {env_file_q}; set +a; "
        + f"mkdir -p {log_dir_q}; exec {exec_start} >> {log_file_q} 2>&1"
    )


def _run_process(
    cmd: list[str],
    *,
    timeout: float,
) -> subprocess.CompletedProcess[str] | None:
    try:
        return subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            check=False,
            timeout=timeout,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
