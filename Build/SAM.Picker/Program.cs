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

// Modified: added the unattended --idle startup mode (supervisor) and --idle-once.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool idleMode = args.Any(a => string.Equals(a, "--idle", StringComparison.OrdinalIgnoreCase));
            bool idleOnceMode = args.Any(a => string.Equals(a, "--idle-once", StringComparison.OrdinalIgnoreCase));

            if (API.Steam.GetInstallPath() == Application.StartupPath)
            {
                if (idleMode == false && idleOnceMode == false)
                {
                    MessageBox.Show(
                        "This tool declines to being run from the Steam directory.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return;
            }

            if (idleOnceMode == true)
            {
                Environment.ExitCode = RunIdleOnce() == true ? 0 : 1;
                return;
            }

            if (idleMode == true)
            {
                RunSupervisor();
                return;
            }

            IdleManager.RepairStartupEntry();

            using (API.Client client = new())
            {
                try
                {
                    client.Initialize(0);
                }
                catch (API.ClientInitializeException e)
                {
                    if (string.IsNullOrEmpty(e.Message) == false)
                    {
                        MessageBox.Show(
                            "Steam is not running. Please start Steam then run this tool again.\n\n" +
                            "(" + e.Message + ")",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else
                    {
                        MessageBox.Show(
                            "Steam is not running. Please start Steam then run this tool again.",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    return;
                }
                catch (DllNotFoundException)
                {
                    MessageBox.Show(
                        "You've caused an exceptional error!",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new GamePicker(client));
            }
        }

        /// <summary>
        /// Unattended mode used by the Windows startup entry (and the Start idling button):
        /// a long-lived, windowless supervisor. It launches the saved idle list once Steam is
        /// ready, and again whenever the Steam session changes (hidden idlers exit on their own
        /// when their session dies). Only one runs at a time. Never shows dialogs.
        /// The Steam client itself is only ever loaded by short-lived --idle-once children,
        /// so this process never keeps an outdated steamclient.dll in memory.
        /// </summary>
        private static void RunSupervisor()
        {
            if (IdleManager.TryBecomeSupervisor(out Mutex mutex) == false)
            {
                // Another supervisor is running; just make it re-read the list.
                IdleManager.SignalReload();
                return;
            }

            using (mutex)
            using (var reload = IdleManager.CreateReloadEvent())
            {
                while (true)
                {
                    if (HasIdleList() == false)
                    {
                        return;
                    }

                    // Wait for Steam, then give it time to finish signing in and loading licences.
                    if (SteamSessionInfo.IsReady(SteamSessionInfo.Read()) == false)
                    {
                        Thread.Sleep(5000);
                        continue;
                    }
                    Thread.Sleep(15000);

                    var session = SteamSessionInfo.Read();
                    if (SteamSessionInfo.IsReady(session) == false)
                    {
                        continue;
                    }

                    reload.Reset();
                    if (LaunchIdleOnce(session) == false)
                    {
                        // Could not talk to Steam; try again in a bit.
                        Thread.Sleep(30000);
                        continue;
                    }

                    while (true)
                    {
                        bool reloadRequested = reload.WaitOne(10000);

                        var current = SteamSessionInfo.Read();
                        if (current.SameAs(session) == false || SteamSessionInfo.IsReady(current) == false)
                        {
                            break;
                        }

                        if (reloadRequested == true)
                        {
                            var list = IdleManager.Load();
                            if (list == null || list.Count == 0)
                            {
                                IdleManager.StopWhere(id => true);
                                return;
                            }

                            IdleManager.StopWhere(id => list.Contains(id) == false);
                            if (LaunchIdleOnce(session) == false)
                            {
                                break;
                            }
                        }
                    }

                    // The session ended. Idlers exit by themselves within ~10 s; kill stragglers.
                    for (int i = 0; i < 30 && IdleManager.GetRunningIds(true).Count > 0; i++)
                    {
                        Thread.Sleep(1000);
                    }
                    IdleManager.StopWhere(id => true);
                }
            }
        }

        private static bool HasIdleList()
        {
            try
            {
                var ids = IdleManager.Load();
                return ids != null && ids.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Runs "--idle-once" and waits for it. Abandons it if the Steam session changes
        /// meanwhile. True when the child finished successfully.
        /// </summary>
        private static bool LaunchIdleOnce(SteamSession session)
        {
            try
            {
                ProcessStartInfo info = new(Application.ExecutablePath, "--idle-once")
                {
                    WorkingDirectory = Application.StartupPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var child = Process.Start(info);
                while (child.WaitForExit(2000) == false)
                {
                    if (SteamSessionInfo.Read().SameAs(session) == false)
                    {
                        child.Kill();
                        return false;
                    }
                }
                return child.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Waits for Steam to accept a client connection, launches the saved idle list hidden
        /// (skipping games already idling) and returns. True unless Steam never became usable.
        /// </summary>
        private static bool RunIdleOnce()
        {
            var ids = IdleManager.Load();
            if (ids == null || ids.Count == 0)
            {
                return true;
            }

            // Steam is often still starting at login; keep trying for up to ~5 minutes.
            for (int attempt = 0; attempt < 30; attempt++)
            {
                API.Client client = new();
                try
                {
                    client.Initialize(0);
                }
                catch (Exception)
                {
                    client.Dispose();
                    Thread.Sleep(10000);
                    continue;
                }

                List<uint> eligible;
                using (client)
                {
                    eligible = ids
                        .Where(id => IdleManager.IsInstalled(client, id))
                        .Take(IdleManager.MaxIdleGames)
                        .ToList();
                }

                IdleManager.Start(eligible, 2000);
                return true;
            }

            return false;
        }
    }
}
