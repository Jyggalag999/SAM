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

// Modified: detects Steam restarts / sign-outs (linked into both SAM.Picker and SAM.Game).

using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace SAM
{
    /// <summary>The running Steam client: its process id and the signed-in account.</summary>
    internal readonly struct SteamSession
    {
        public readonly int Pid;
        public readonly int ActiveUser;

        public SteamSession(int pid, int activeUser)
        {
            this.Pid = pid;
            this.ActiveUser = activeUser;
        }

        public bool SameAs(SteamSession other)
        {
            return this.Pid == other.Pid && this.ActiveUser == other.ActiveUser;
        }
    }

    internal static class SteamSessionInfo
    {
        private const string KeyPath = @"Software\Valve\Steam\ActiveProcess";

        /// <summary>Reads Steam's live state from the registry. Never throws; zeros on any error.</summary>
        public static SteamSession Read()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
                if (key == null)
                {
                    return new SteamSession(0, 0);
                }

                int pid = key.GetValue("pid") is int p ? p : 0;
                int user = key.GetValue("ActiveUser") is int u ? u : 0;
                return new SteamSession(pid, user);
            }
            catch (Exception)
            {
                return new SteamSession(0, 0);
            }
        }

        /// <summary>True when Steam is running (the recorded pid is a live steam process) and a user is signed in.</summary>
        public static bool IsReady(SteamSession session)
        {
            if (session.Pid == 0 || session.ActiveUser == 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(session.Pid);
                return string.Equals(process.ProcessName, "steam", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // No such process (ArgumentException) or access problem.
                return false;
            }
        }
    }
}
