/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

// Modified: idle-list storage, startup registration and background idling.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SAM.Picker
{
    /// <summary>
    /// Everything about idling that does not need the picker window: where the idle
    /// list lives, which games may be idled, launching/stopping SAM.Game background
    /// processes and the "start with Windows" registry entry. All paths are derived
    /// from where the executable is at runtime, so the folder can live anywhere.
    /// </summary>
    internal static class IdleManager
    {
        public const int MaxIdleGames = 30;

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "SAM Auto Idle";

        public static string IdleListPath => Path.Combine(Application.StartupPath, "idle-list.txt");
        public static string GameExePath => Path.Combine(Application.StartupPath, "SAM.Game.exe");

        /// <summary>A game can idle when Steam reports it installed; which ones is the user's choice.</summary>
        public static bool IsInstalled(API.Client client, uint id)
        {
            try
            {
                return client.SteamApps008.IsAppInstalled(id);
            }
            catch
            {
                // Fail closed: never idle a game if installation status is unknown.
                return false;
            }
        }

        /// <summary>Returns the IDs in file order, or null when the file does not exist yet.</summary>
        public static List<uint> Load()
        {
            if (File.Exists(IdleListPath) == false)
            {
                return null;
            }

            List<uint> ids = new();
            foreach (var rawLine in File.ReadAllLines(IdleListPath))
            {
                var line = rawLine.Split('#')[0].Trim();
                if (line.Length > 0 &&
                    uint.TryParse(line, out uint id) == true &&
                    ids.Contains(id) == false)
                {
                    ids.Add(id);
                }
            }
            return ids;
        }

        public static void Save(IEnumerable<uint> ids, Func<uint, string> nameOf)
        {
            List<string> lines = new()
            {
                "# Games SAM idles (\"Start idling\" button and Windows startup).",
                "# Managed by SAM.Picker -- edits here are overwritten when the list changes.",
                "# One Steam App ID per line, optional trailing \"# name\" comment.",
            };

            foreach (var id in ids)
            {
                var name = nameOf(id);
                var idText = id.ToString(CultureInfo.InvariantCulture);
                lines.Add(string.IsNullOrEmpty(name) == false ? $"{idText} # {name}" : idText);
            }

            File.WriteAllLines(IdleListPath, lines);
        }

        private static IEnumerable<ManagementObject> QueryBackgroundGameProcesses()
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE Name = 'SAM.Game.exe'");
            foreach (ManagementObject process in searcher.Get())
            {
                var commandLine = process["CommandLine"] as string ?? "";
                if (commandLine.IndexOf("--background", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    yield return process;
                }
            }
        }

        private static bool TryGetAppId(ManagementObject process, out uint id)
        {
            var match = Regex.Match(process["CommandLine"] as string ?? "", @"(\d+)\s+--background");
            id = 0;
            return match.Success && uint.TryParse(match.Groups[1].Value, out id);
        }

        /// <summary>True when the process runs the SAM.Game.exe next to this executable.</summary>
        private static bool IsFromThisFolder(ManagementObject process)
        {
            try
            {
                return process["ExecutablePath"] is string path &&
                    string.Equals(Path.GetFullPath(path), Path.GetFullPath(GameExePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>App IDs that currently have a hidden SAM.Game idling them (from any launcher).</summary>
        public static HashSet<uint> GetRunningIds()
        {
            return GetRunningIds(false);
        }

        /// <param name="thisFolderOnly">Only count idlers started from this folder's SAM.Game.exe.</param>
        public static HashSet<uint> GetRunningIds(bool thisFolderOnly)
        {
            HashSet<uint> ids = new();
            try
            {
                foreach (var process in QueryBackgroundGameProcesses())
                {
                    if (thisFolderOnly == true && IsFromThisFolder(process) == false)
                    {
                        continue;
                    }

                    if (TryGetAppId(process, out uint id))
                    {
                        ids.Add(id);
                    }
                }
            }
            catch (Exception)
            {
                // WMI unavailable; report nothing running rather than failing.
            }
            return ids;
        }

        /// <summary>
        /// Stops the idlers launched from this folder whose app ID satisfies the predicate
        /// (idlers from other folders are left alone). Used by the supervisor.
        /// </summary>
        public static int StopWhere(Func<uint, bool> shouldStop)
        {
            int stopped = 0;
            try
            {
                foreach (var process in QueryBackgroundGameProcesses())
                {
                    if (IsFromThisFolder(process) == false ||
                        TryGetAppId(process, out uint id) == false ||
                        shouldStop(id) == false)
                    {
                        continue;
                    }

                    try
                    {
                        using var p = Process.GetProcessById(Convert.ToInt32(process["ProcessId"], CultureInfo.InvariantCulture));
                        p.Kill();
                        stopped++;
                    }
                    catch (Exception)
                    {
                        // Already exited.
                    }
                }
            }
            catch (Exception)
            {
                // WMI unavailable.
            }
            return stopped;
        }

        private const string SupervisorMutexName = @"Local\SAM.IdleSupervisor";
        private const string SupervisorReloadEventName = @"Local\SAM.IdleSupervisor.Reload";

        /// <summary>Creates the single-instance mutex. False when another supervisor already holds it.</summary>
        public static bool TryBecomeSupervisor(out Mutex mutex)
        {
            mutex = new Mutex(true, SupervisorMutexName, out bool created);
            if (created == false)
            {
                mutex.Dispose();
                mutex = null;
            }
            return created;
        }

        public static EventWaitHandle CreateReloadEvent()
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, SupervisorReloadEventName);
        }

        public static bool IsSupervisorRunning()
        {
            if (Mutex.TryOpenExisting(SupervisorMutexName, out Mutex mutex) == true)
            {
                mutex.Dispose();
                return true;
            }
            return false;
        }

        /// <summary>Tells a running supervisor to re-read the idle list now. No-op when none runs.</summary>
        public static void SignalReload()
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(SupervisorReloadEventName, out EventWaitHandle handle) == true)
                {
                    using (handle)
                    {
                        handle.Set();
                    }
                }
            }
            catch (Exception)
            {
                // Not critical; the supervisor also re-reads the list on its own schedule.
            }
        }

        /// <summary>Starts the idle supervisor (SAM.Picker.exe --idle), or asks the running one to reload the list.</summary>
        public static void StartSupervisor()
        {
            if (IsSupervisorRunning() == true)
            {
                SignalReload();
                return;
            }

            try
            {
                ProcessStartInfo info = new(Application.ExecutablePath, "--idle")
                {
                    WorkingDirectory = Application.StartupPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(info);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }

        /// <summary>Kills the supervisor and any --idle-once helper it is running (this picker window is left alone).</summary>
        public static int StopSupervisor()
        {
            int stopped = 0;
            try
            {
                int ownPid;
                using (var current = Process.GetCurrentProcess())
                {
                    ownPid = current.Id;
                }

                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'SAM.Picker.exe'");
                foreach (ManagementObject process in searcher.Get())
                {
                    var commandLine = process["CommandLine"] as string ?? "";
                    int pid = Convert.ToInt32(process["ProcessId"], CultureInfo.InvariantCulture);
                    // "--idle" also matches "--idle-once".
                    if (commandLine.IndexOf("--idle", StringComparison.OrdinalIgnoreCase) < 0 ||
                        pid == ownPid)
                    {
                        continue;
                    }

                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        p.Kill();
                        stopped++;
                    }
                    catch (Exception)
                    {
                        // Already exited.
                    }
                }
            }
            catch (Exception)
            {
                // WMI unavailable.
            }
            return stopped;
        }

        /// <summary>Launches a hidden SAM.Game for each ID not already idling. Returns how many started.</summary>
        public static int Start(IEnumerable<uint> ids, int delayMilliseconds)
        {
            var running = GetRunningIds();
            int started = 0;

            foreach (var id in ids.Take(MaxIdleGames))
            {
                if (running.Contains(id))
                {
                    continue;
                }

                try
                {
                    ProcessStartInfo info = new(GameExePath, $"{id.ToString(CultureInfo.InvariantCulture)} --background")
                    {
                        WorkingDirectory = Application.StartupPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var process = Process.Start(info);
                    started++;
                }
                catch (Exception)
                {
                    // Best effort; a single bad launch shouldn't stop the rest.
                    continue;
                }

                if (delayMilliseconds > 0)
                {
                    System.Threading.Thread.Sleep(delayMilliseconds);
                }
            }

            return started;
        }

        /// <summary>Stops every hidden SAM.Game, including ones started at Windows login.</summary>
        public static int StopAll()
        {
            int stopped = 0;
            try
            {
                foreach (var process in QueryBackgroundGameProcesses())
                {
                    try
                    {
                        using var p = Process.GetProcessById(Convert.ToInt32(process["ProcessId"], CultureInfo.InvariantCulture));
                        p.Kill();
                        stopped++;
                    }
                    catch (Exception)
                    {
                        // Already exited.
                    }
                }
            }
            catch (Exception)
            {
                // WMI unavailable.
            }
            return stopped;
        }

        private static string StartupCommand => $"\"{Application.ExecutablePath}\" --idle";

        public static bool IsStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(RunValueName) is string;
        }

        public static void SetStartupEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (enabled)
            {
                key.SetValue(RunValueName, StartupCommand);
            }
            else if (key.GetValue(RunValueName) != null)
            {
                key.DeleteValue(RunValueName, false);
            }
        }

        /// <summary>If startup is on but the folder moved, point the entry at the current location.</summary>
        public static void RepairStartupEntry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key?.GetValue(RunValueName) is string current &&
                    string.Equals(current, StartupCommand, StringComparison.OrdinalIgnoreCase) == false)
                {
                    key.SetValue(RunValueName, StartupCommand);
                }
            }
            catch (Exception)
            {
                // Not critical.
            }
        }
    }
}
