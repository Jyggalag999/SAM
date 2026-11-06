# SAM Auto-Idle

A modified version of [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) by gibbed. It keeps everything the original does and adds batch achievement unlocking and game idling: pick up to 30 games and SAM keeps them running hidden in the background, so Steam shows them as "In game" and counts the playtime. It can also start idling automatically when Windows starts.

**[Download the latest release](https://github.com/Jyggalag999/SAM/releases/latest)**

## Features

- **Idle list.** Choose up to 30 installed games from a searchable checklist and start them with one click. They run hidden, with no windows, and keep going after you close SAM.
- **Idle at startup (optional).** Tick one box and SAM starts idling when you sign in to Windows. It waits for Steam to be running and signed in first.
- **Survives Steam restarts.** When Steam restarts, updates or signs out, the idlers close themselves. SAM starts them again once Steam is back.
- **Live status.** A green dot marks each game that is idling right now, and the panel shows "waiting for Steam..." while Steam is down.
- **Batch unlock.** Achievements → Batch unlock all listed games unlocks every achievement in every game currently shown in the list, except protected ones, which SAM can't change. Use search and filters to narrow the list first. It runs one game at a time, shows progress, and can be cancelled at any point.
- **Dark redesigned UI** across the game picker and the achievement manager.
- **Portable.** No installer, no hardcoded paths. Everything lives in its own folder.
- **Original SAM features.** Double-click a game to view and change its achievements and stats.

## Getting started

1. Download `SAM-AutoIdle.zip` from the releases page and unzip it somewhere permanent, such as `Documents\SAM`. Don't put it inside the Steam folder.
2. Make sure Steam is running and you're signed in.
3. Run `SAM.Picker.exe`. Windows SmartScreen may warn you because the exe isn't signed. If it does, click **More info**, then **Run anyway**.
4. Click **Choose games...**, tick the games you want, and click **Save**.
5. Click **Start idling**. To idle on every boot, tick **Start idling when Windows starts**.

**Stop all** stops every idler and the background helper.

## How it works

- **Idlers.** Each idled game is a hidden copy of `SAM.Game.exe <appid> --background`. It connects to Steam as that game, which is what makes Steam show you as playing it.
- **Supervisor.** `SAM.Picker.exe --idle` runs quietly in the background. It waits for Steam, launches the games on your list, and watches the Steam session. If Steam restarts, the old idlers exit on their own and the supervisor relaunches them once Steam is ready again.
- **Startup.** The startup option adds one entry, `SAM Auto Idle`, to your Windows startup apps (`HKCU\...\Run`). It points at the supervisor in whatever folder SAM is in. Unticking the option removes it.
- **Batch unlock.** SAM runs `SAM.Game.exe <appid> --auto-unlock` for each listed game in turn. That unlocks the game's achievements, saves them to Steam, and closes before the next game starts.
- **Your list.** The games you chose are saved to `idle-list.txt` next to the exe. That file and the optional startup entry are the only things SAM writes.

## Removing it

Press **Stop all**, untick the startup option, and delete the folder.

## Building from source

The project targets .NET Framework 4.8 and x86.

```
dotnet build Build/SAM.sln -c Release -p:Platform=x86
```

To build a release zip in `Build/dist/`, run:

```
powershell -ExecutionPolicy Bypass -File Build/package.ps1
```

## Notes

- Changing achievements is a gray area. Valve doesn't officially support it, and some players, games or communities may treat unlocked achievements as cheating. Use it at your own risk.
- This is an altered version of SAM, not the original. The original is © Rick "gibbed" and zlib-licensed; see [LICENSE.txt](LICENSE.txt). Icons are from [Fugue Icons](https://p.yusukekamiyamane.com/) by Yusuke Kamiyamane (CC BY 3.0).
