# Plan: survive Steam restarts (reconnect + auto-kill stale idlers)

Status: ready to build. Written 2026-11-05 by the design session; build in a fresh session.

## Problem

Each hidden idler (`SAM.Game.exe <appid> --background`) connects to Steam once, at
start. When Steam restarts (update, crash, sign-out, user quits), every idler keeps
running but its Steam connection is dead, so Steam shows nothing "In game". The
picker still shows them as idling (green dots), because it only counts processes.
Observed on 2026-11-05: 30 idlers started Nov 3, Steam restarted Nov 4, nothing idled
for a day.

## Signal: how to detect a Steam restart

Steam writes its live state to the registry (verified on this machine; values matched
the running `steam.exe`):

- `HKCU\Software\Valve\Steam\ActiveProcess` value `pid` (DWORD): PID of the running
  `steam.exe`; 0 or stale when Steam is not running.
- same key, value `ActiveUser` (DWORD): Steam account id of the signed-in user; 0 when
  signed out or still signing in.

"Steam session" = (pid, ActiveUser). It is *ready* when pid != 0, a process with that
PID exists and is named `steam`, and ActiveUser != 0. A restart = the pid changes. A
sign-out or account switch = ActiveUser changes or becomes 0.

## Design

Never try to re-use a dead connection inside an old process: `steamclient.dll` is
loaded from the Steam folder, and Steam updates replace it, so a long-lived process
would keep an outdated copy in memory. Instead: **idlers die with their Steam session,
and one supervisor relaunches them.**

### 1. Shared helper `Build/Shared/SteamSession.cs` (linked into both exes, like Theme.cs)

```csharp
internal readonly struct SteamSession { public int Pid; public int ActiveUser; }
internal static class SteamSessionInfo {
    public static SteamSession Read();          // registry read; never throws (0s on error)
    public static bool IsReady(SteamSession s); // pid != 0 && process alive && name == "steam" && ActiveUser != 0
}
```

Add `<Compile Include="..\Shared\SteamSession.cs" Link="SteamSession.cs" />` to both
csproj files.

### 2. Idler watchdog: `SAM.Game` background mode exits with its session

In `Manager` (background mode only), remember `SteamSessionInfo.Read()` right after the
client initializes. Add a `System.Windows.Forms.Timer` (10 s). On each tick, if the
current session's Pid or ActiveUser differs from the remembered one, or `IsReady` is
false, call `Application.Exit()`. Not in foreground mode (the user may be editing
achievements; leave it alone).

Result: no zombie idlers, whoever launched them. The picker's green dots become accurate
automatically, because it counts live processes.

### 3. Supervisor: `SAM.Picker.exe --idle` becomes long-lived

Today `--idle` (Program.RunIdleMode) launches the list once and exits. Change it to:

- Single instance: named mutex `Local\SAM.IdleSupervisor`. If it is already held, signal
  the reload event (below) and exit.
- Loop forever (no window, no dialogs, nothing on the UI thread):
  1. Wait until `IsReady` (poll every 5 s). After it becomes ready, wait a 15 s grace
     period (Steam finishes signing in and loading licences).
  2. Spawn a short-lived child `SAM.Picker.exe --idle-once` and wait for it to exit.
     `--idle-once` is today's RunIdleMode unchanged (init client, filter installed,
     `IdleManager.Start` skipping already-running ids, exit). The child loads
     steamclient.dll, so the supervisor itself never does.
  3. Remember the session. Every 10 s: if the session changed or is not ready, wait for
     this list's idlers to exit (the watchdog does it; after 30 s, kill any leftovers via
     `IdleManager.StopAll()`), then go to step 1.
  4. Also every 10 s, if the reload event is set: run `--idle-once` again (it starts only
     games not already idling), so newly added games start. Games removed from the list:
     kill their idlers (compare `IdleManager.GetRunningIds()` with `IdleManager.Load()`).
- Named `EventWaitHandle` `Local\SAM.IdleSupervisor.Reload` (auto-reset) for step 4.
- Exit the supervisor when the idle list file is empty or missing (nothing to do).

Startup entry stays `"<exe>" --idle`, so existing installs pick this up automatically.

### 4. Picker UI wiring (`GamePicker.IdlePanel.cs`, `IdleManager.cs`)

- `IdleManager.StartSupervisor()`: if the mutex is free, launch
  `SAM.Picker.exe --idle` (UseShellExecute=false, CreateNoWindow); otherwise signal the
  reload event.
- **Start idling** button: save the list, then `StartSupervisor()` instead of calling
  `IdleManager.Start` directly. Keep the status-label text.
- Saving the idle list (choose games, remove, clear) while the supervisor runs: signal
  reload so the change applies live.
- **Stop all**: first stop the supervisor (kill the `SAM.Picker.exe` whose command line
  contains `--idle` but not `--idle-once`, via the same WMI query style as
  `QueryBackgroundGameProcesses`), then `StopAll()` the idlers. Otherwise the supervisor
  would just restart them.
- Count label: if the supervisor is running and the Steam session is not ready, show
  "waiting for Steam..." instead of "not idling".

### 5. Docs

`HOW TO USE.txt`: one line under Idling: idling now resumes on its own after Steam
restarts or updates.

## Files

| File | Change |
|---|---|
| `Build/Shared/SteamSession.cs` | new |
| `Build/SAM.Game/SAM.Game.csproj`, `Build/SAM.Picker/SAM.Picker.csproj` | link the new file |
| `Build/SAM.Game/Manager.cs` | background watchdog timer |
| `Build/SAM.Picker/Program.cs` | `--idle` supervisor loop, `--idle-once` = old behaviour |
| `Build/SAM.Picker/IdleManager.cs` | `StartSupervisor`, `StopSupervisor`, `IsSupervisorRunning`, reload signal |
| `Build/SAM.Picker/GamePicker.IdlePanel.cs` | Start/Stop/save wiring, "waiting for Steam" |
| `Build/HOW TO USE.txt` | one line |

## Constraints

- Match existing style (`this.`, `== false`, braces, zlib header with a "Modified:" line on new files).
- No new NuGet packages. `System.Management` is already referenced by the picker.
- Never show dialogs in `--idle`, `--idle-once` or `--background` modes.
- Don't touch the user's running idlers in `F:\SAM\Build\upload\` while testing. Build
  Debug (`dotnet build Build/SAM.sln -c Debug -p:Platform=x86`, output `Build/bin/`) and
  test from there. `Build/package.ps1` makes the release zip.

## Verification

1. Build succeeds with no new warnings.
2. Watchdog: start `bin\SAM.Game.exe <appid> --background`, then temporarily write a
   different `pid` to `HKCU\Software\Valve\Steam\ActiveProcess` (restore the real value
   right after). The idler must exit within ~10 s. This avoids restarting the user's Steam.
3. Supervisor: put 2 installed app IDs in `bin\idle-list.txt`, run `bin\SAM.Picker.exe
   --idle`. Both idlers start. Run `--idle` again: the second copy exits, nothing doubles.
   Do the pid trick: the idlers exit, and after the grace period they come back with the
   new session (restore the real pid first so `IsReady` passes).
4. UI: Start idling launches the supervisor; Stop all leaves no `SAM.Picker --idle` and
   no `--background` processes from `bin\`; changing the list while idling adds/removes
   the right games.
5. Kill only the processes you started from `bin\`. Never `StopAll()` while the user's
   `upload\` idlers are running unless the user agrees: it stops all of them.
6. Commit on a branch, report, and leave merging/pushing/release to the user.
