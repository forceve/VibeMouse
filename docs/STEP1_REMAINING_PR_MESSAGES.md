# [5/7] windows merge-in

## What
Merge the Windows implementation into the mainline package on top of the new runtime boundary.

- `vibemouse/platform/system_integration.py` for Win32 active-window, text-input, workspace, and cursor integration
- `vibemouse/listener/keyboard_listener.py` for the Windows listener path
- `vibemouse/ops/deploy.py` and `vibemouse/ops/doctor.py` for Windows deploy and health-check flows
- `vibemouse/config/schema.py` for Windows-facing config surface updates
- Windows coverage expanded in `tests/platform/test_system_integration.py`, `tests/listener/test_keyboard_listener.py`, `tests/ops/test_deploy.py`, `tests/ops/test_doctor.py`, and related runtime tests

## Why
Windows support should land on top of the reviewed config, bindings, and IPC boundaries so platform behavior can be reviewed separately from protocol/runtime changes and without keeping a fork-only implementation path.

## Stack
[1/7]-[4/7] config / files / bindings / ipc runtime
-> **[5/7] windows merge-in** <- you are here
-> [6/7] macos port
-> [7/7] panel tests ci

# [6/7] macos port

## What
Add macOS support on the same mainline runtime boundary after Windows is merged in.

- `vibemouse/platform/system_integration.py` for macOS window, cursor, and accessibility-backed integration
- `vibemouse/ops/deploy.py` and `vibemouse/ops/doctor.py` for launchd deployment and macOS diagnostics
- `vibemouse/core/output.py` for macOS paste/output routing
- `vibemouse/listener/keyboard_listener.py` for macOS listener compatibility
- macOS coverage expanded in `tests/platform/test_system_integration.py`, `tests/ops/test_deploy.py`, and `tests/ops/test_doctor.py`

## Why
The third platform should be reviewed as its own step after Windows so macOS-specific integration stays isolated from both the IPC/runtime review and the final panel/CI cleanup.

## Stack
[1/7]-[5/7] config / files / bindings / ipc runtime / windows merge-in
-> **[6/7] macos port** <- you are here
-> [7/7] panel tests ci

# [7/7] panel tests ci

## What
Close Step 1 with the panel boundary, final test-tree cleanup, and full cross-platform CI.

- `panel/VibeMouse.Panel/...` for the Step-1 narrow panel boundary: read `status.json`, edit `config.json`, and trigger safe agent-side actions without reaching into runtime internals
- `.github/workflows/ci.yml` for a Linux / Windows / macOS matrix covering both `pytest` and `dotnet build`
- `tests/config/` and the remaining test moves to finish responsibility-based test layout cleanup
- `README.md`, `README.zh-CN.md`, `docs/AI_ASSISTANT_DEPLOYMENT.md`, and `docs/AI_ASSISTANT_DEPLOYMENT.zh-CN.md` to document the merged cross-platform mainline
- Small follow-up test/runtime fixes needed to keep the final matrix stable

## Why
Once runtime semantics and platform ports are stable, panel scope, test organisation, and CI expansion can be reviewed as one closing quality pass instead of being mixed into the platform PRs.

## Stack
[1/7]-[6/7] config / files / bindings / ipc runtime / windows merge-in / macos port
-> **[7/7] panel tests ci** <- you are here
