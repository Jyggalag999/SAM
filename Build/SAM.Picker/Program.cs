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

// Modified: added the unattended --idle startup mode.

using System;
using System.Collections.Generic;
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

            if (API.Steam.GetInstallPath() == Application.StartupPath)
            {
                if (idleMode == false)
                {
                    MessageBox.Show(
                        "This tool declines to being run from the Steam directory.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return;
            }

            if (idleMode == true)
            {
                RunIdleMode();
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
        /// Unattended mode used by the Windows startup entry: wait for Steam to be running
        /// and signed in, launch the saved idle list hidden, then exit. Never shows dialogs.
        /// </summary>
        private static void RunIdleMode()
        {
            var ids = IdleManager.Load();
            if (ids == null || ids.Count == 0)
            {
                return;
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
                return;
            }
        }
    }
}
