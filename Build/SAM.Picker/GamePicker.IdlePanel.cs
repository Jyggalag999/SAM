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

// Modified: dark theme, menu bar and the idle panel (right-hand side of the picker).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal partial class GamePicker
    {
        private Panel _IdlePanel;
        private Label _IdleCountLabel;
        private ListBox _IdleListBox;
        private Label _IdleEmptyLabel;
        private Button _StartIdlingButton;
        private Button _StopIdlingButton;
        private CheckBox _StartupCheckBox;
        private ToolStripMenuItem _StartupMenuItem;
        private ToolStripMenuItem _BatchUnlockMenuItem;
        private Timer _IdleStatusTimer;

        private HashSet<uint> _RunningIds = new();
        private readonly HashSet<uint> _InstalledIds = new();
        private bool _RefreshingRunning;
        private bool _UpdatingStartup;

        private sealed class IdleEntry
        {
            public uint Id;
            public string Name;
            public override string ToString() => this.Name;
        }

        // ---------------------------------------------------------------- layout

        /// <summary>Called from InitializeComponent, before the panel is added to the form.</summary>
        private void BuildIdlePanel()
        {
            var title = new Label
            {
                Text = "Idle List",
                AutoSize = true,
                Font = Theme.TitleFont,
                ForeColor = Theme.Text,
                Margin = new Padding(0, 0, 0, 2),
            };

            this._IdleCountLabel = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Muted,
                Margin = new Padding(0, 0, 0, 10),
            };

            this._IdleListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Back,
                ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.UiFont.Height + 12, // scales with DPI
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended,
            };
            Theme.DarkScrollBars(this._IdleListBox);
            this._IdleListBox.DrawItem += this.OnIdleListBoxDrawItem;
            this._IdleListBox.DoubleClick += (s, e) => this.RemoveSelectedIdleEntries();
            this._IdleListBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    this.RemoveSelectedIdleEntries();
                    e.Handled = true;
                }
            };

            this._IdleEmptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No games yet.\n\nClick \"Choose games\" below\nto pick what to idle.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                BackColor = Theme.Back,
                Cursor = Cursors.Hand,
            };
            this._IdleEmptyLabel.Click += (s, e) => this.OpenIdleChooser();

            var listHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = Theme.Border,
                Margin = new Padding(0, 0, 0, 8),
            };
            listHost.Controls.Add(this._IdleListBox);
            listHost.Controls.Add(this._IdleEmptyLabel);
            this._IdleEmptyLabel.BringToFront();

            var choose = Theme.MakeButton("Choose games...", Theme.Accent, (s, e) => this.OpenIdleChooser());
            choose.Font = Theme.ButtonFont;
            var remove = Theme.MakeButton("Remove", Theme.Surface2, (s, e) => this.RemoveSelectedIdleEntries());
            var clear = Theme.MakeButton("Clear", Theme.Surface2, this.OnClearIdleList);
            foreach (var b in new[] { remove, clear })
            {
                b.Margin = new Padding(0, 4, 6, 6);
            }

            var listButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            listButtons.Controls.AddRange(new Control[] { choose, remove, clear });

            this._StartIdlingButton = Theme.MakeButton("▶  Start idling", Theme.Green, this.OnStartIdling);
            this._StopIdlingButton = Theme.MakeButton("■  Stop all", Theme.Red, this.OnStopIdling);
            foreach (var button in new[] { this._StartIdlingButton, this._StopIdlingButton })
            {
                button.AutoSize = false;
                button.Dock = DockStyle.Fill;
                button.Font = new Font("Segoe UI Semibold", 11F);
            }
            this._StartIdlingButton.Margin = new Padding(0, 0, 4, 0);
            this._StopIdlingButton.Margin = new Padding(4, 0, 0, 0);

            var actionButtons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 10),
            };
            actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            actionButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actionButtons.Controls.Add(this._StartIdlingButton, 0, 0);
            actionButtons.Controls.Add(this._StopIdlingButton, 1, 0);

            this._StartupCheckBox = new CheckBox
            {
                Text = "Start idling when Windows starts",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
            };
            Theme.StyleCheckBox(this._StartupCheckBox);
            this._StartupCheckBox.CheckedChanged += this.OnStartupCheckBoxChanged;

            var hint = new Label
            {
                Text = "Steam must be running. Games keep idling after you close this window. " +
                       "Double-click a game on the left to edit its achievements.",
                AutoSize = true,
                MaximumSize = new Size(290, 0),
                Font = Theme.SmallFont,
                ForeColor = Theme.Muted,
                Margin = new Padding(0),
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(this._IdleCountLabel, 0, 1);
            layout.Controls.Add(listHost, 0, 2);
            layout.Controls.Add(listButtons, 0, 3);
            layout.Controls.Add(actionButtons, 0, 4);
            layout.Controls.Add(this._StartupCheckBox, 0, 5);
            layout.Controls.Add(hint, 0, 6);

            this._IdlePanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 320,
                Padding = new Padding(14, 12, 14, 12),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.UiFont,
            };
            this._IdlePanel.Controls.Add(layout);

            this._IdleStatusTimer = new Timer(this.components) { Interval = 4000, Enabled = true };
            this._IdleStatusTimer.Tick += (s, e) => this.RefreshRunningState();
        }

        /// <summary>Dark theme, menu bar and a simpler toolbar. Called after InitializeComponent.</summary>
        private void ApplyTheme()
        {
            Theme.ApplyForm(this);

            this._GameListView.BackColor = Theme.Back;
            this._GameListView.ForeColor = Theme.Text;
            this._GameListView.BorderStyle = BorderStyle.None;
            Theme.DarkScrollBars(this._GameListView);

            // Toolbar: search on the left, add-by-ID on the right. Filters and batch
            // unlock move to the menu bar.
            var bar = this._PickerToolStrip;
            bar.Items.Clear();
            this._SearchGameTextBox.AutoSize = false;
            this._SearchGameTextBox.Width = 300;
            this._RefreshGamesButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            this._RefreshGamesButton.ToolTipText = "Refresh game list (F5)";
            this._AddGameButton.Alignment = ToolStripItemAlignment.Right;
            this._AddGameButton.Text = "Add";
            this._AddGameButton.ToolTipText = "Show only the game with this Steam App ID";
            this._AddGameTextBox.Alignment = ToolStripItemAlignment.Right;
            this._AddGameTextBox.AutoSize = false;
            this._AddGameTextBox.Width = 90;
            this._AddGameTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    this.OnAddGame(s, e);
                    e.SuppressKeyPress = true;
                }
            };
            this._BatchUnlockButton.Visible = false;
            this._BatchUnlockButton.Text = "Cancel batch unlock";
            bar.Items.AddRange(new ToolStripItem[]
            {
                this._SearchGameTextBox, this._RefreshGamesButton, this._BatchUnlockButton,
                this._AddGameButton, this._AddGameTextBox,
            });
            Theme.ApplyToolStrip(bar);
            Theme.SetCueBanner(this._SearchGameTextBox.TextBox, "Search your games...");
            Theme.SetCueBanner(this._AddGameTextBox.TextBox, "App ID");

            Theme.ApplyToolStrip(this._PickerStatusStrip);
            this._PickerStatusLabel.ForeColor = Theme.Muted;
            Theme.ApplyContextMenu(this._GameContextMenuStrip);

            var menu = this.BuildMenuStrip();
            Theme.ApplyToolStrip(menu);
            this.MainMenuStrip = menu;
            this.Controls.Add(menu); // added last, so it docks above the toolbar
        }

        private MenuStrip BuildMenuStrip()
        {
            ToolStripMenuItem Item(string text, EventHandler onClick, Keys keys = Keys.None)
            {
                var item = new ToolStripMenuItem(text, null, onClick);
                if (keys != Keys.None)
                {
                    item.ShortcutKeys = keys;
                }
                return item;
            }

            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("&Refresh game list", this.OnRefresh, Keys.F5),
                Item("Open SAM &folder", (s, e) => Process.Start("explorer.exe", $"\"{Application.StartupPath}\"")),
                new ToolStripSeparator(),
                Item("E&xit", (s, e) => this.Close()),
            });

            // The type filters used to live in a toolbar drop-down.
            var view = new ToolStripMenuItem("&View");
            var filters = this._FilterDropDownButton.DropDownItems.Cast<ToolStripItem>().ToArray();
            view.DropDownItems.AddRange(filters);

            var idle = new ToolStripMenuItem("&Idle");
            var addSelected = Item("Add &selected games to idle list", (s, e) => this.AddSelectedGamesToIdleList());
            addSelected.ShortcutKeyDisplayString = "Insert";
            this._StartupMenuItem = Item("Start idling when &Windows starts", (s, e) =>
                this._StartupCheckBox.Checked = !this._StartupCheckBox.Checked);
            idle.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("&Choose games to idle...", (s, e) => this.OpenIdleChooser(), Keys.Control | Keys.I),
                addSelected,
                new ToolStripSeparator(),
                Item("S&tart idling", this.OnStartIdling, Keys.Control | Keys.Enter),
                Item("Sto&p all idling", this.OnStopIdling),
                new ToolStripSeparator(),
                this._StartupMenuItem,
            });

            var achievements = new ToolStripMenuItem("&Achievements");
            var open = Item("&Open selected game", (s, e) =>
            {
                var game = this.GetSelectedGames().FirstOrDefault();
                if (game != null)
                {
                    this.LaunchManager(game);
                }
            });
            open.ShortcutKeyDisplayString = "Enter";
            this._BatchUnlockMenuItem = Item("&Batch unlock all listed games...", this.OnBatchUnlock);
            achievements.DropDownItems.AddRange(new ToolStripItem[] { open, new ToolStripSeparator(), this._BatchUnlockMenuItem });

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("&How to use", this.OnHowToUse, Keys.F1),
                Item("&About", this.OnAbout),
            });

            var menu = new MenuStrip { Padding = new Padding(6, 3, 0, 3) };
            menu.Items.AddRange(new ToolStripItem[] { file, view, idle, achievements, help });
            return menu;
        }

        private void OnHowToUse(object sender, EventArgs e)
        {
            var path = Path.Combine(Application.StartupPath, "HOW TO USE.txt");
            if (File.Exists(path))
            {
                Process.Start(path);
                return;
            }

            MessageBox.Show(this,
                "1. Click \"Choose games...\" and tick the games you want to idle.\n" +
                "2. Click \"Start idling\". You can close SAM afterwards.\n" +
                "3. Tick \"Start idling when Windows starts\" to do it automatically.\n\n" +
                "Double-click a game to edit its achievements.",
                "How to use", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnAbout(object sender, EventArgs e)
        {
            MessageBox.Show(this,
                "Steam Achievement Manager 7.0 -- modified version with auto-idle.\n\n" +
                "Original by Rick \"gibbed\": https://github.com/gibbed/SteamAchievementManager\n" +
                "Released under the zlib license. This is an altered version and is not the original software.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------------------------------------------------------------- state

        private string IdleGameName(uint id)
        {
            if (this._Games.TryGetValue(id, out var game) == true)
            {
                return game.Name;
            }

            string name = null;
            try
            {
                name = this._SteamClient.SteamApps001.GetAppData(id, "name");
            }
            catch (Exception)
            {
            }
            return string.IsNullOrEmpty(name) == false ? name : $"App {id}";
        }

        private void InitializeIdlePanel()
        {
            bool enabled = false;
            try
            {
                enabled = IdleManager.IsStartupEnabled();
            }
            catch (Exception)
            {
                this._StartupCheckBox.Enabled = false;
                this._StartupMenuItem.Enabled = false;
            }
            this.SetStartupUi(enabled);

            this.RefreshIdlePanel();
            this.RefreshRunningState();
        }

        private void SetStartupUi(bool enabled)
        {
            this._UpdatingStartup = true;
            this._StartupCheckBox.Checked = enabled;
            this._StartupMenuItem.Checked = enabled;
            this._UpdatingStartup = false;
        }

        private void RefreshIdlePanel()
        {
            var selected = new HashSet<uint>(this._IdleListBox.SelectedItems.Cast<IdleEntry>().Select(e => e.Id));

            this._InstalledIds.Clear();
            foreach (var id in this._IdleList.Where(id => IdleManager.IsInstalled(this._SteamClient, id)))
            {
                this._InstalledIds.Add(id);
            }

            this._IdleListBox.BeginUpdate();
            this._IdleListBox.Items.Clear();
            foreach (var entry in this._IdleList
                .Select(id => new IdleEntry { Id = id, Name = this.IdleGameName(id) })
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                int index = this._IdleListBox.Items.Add(entry);
                if (selected.Contains(entry.Id))
                {
                    this._IdleListBox.SetSelected(index, true);
                }
            }
            this._IdleListBox.EndUpdate();
            this._IdleEmptyLabel.Visible = this._IdleList.Count == 0;

            this.UpdateIdleCountLabel();
            this._GameListView.Invalidate();
        }

        private void UpdateIdleCountLabel()
        {
            int idling = this._IdleList.Count(id => this._RunningIds.Contains(id));
            int otherIdling = this._RunningIds.Count(id => this._IdleList.Contains(id) == false);

            string text = $"{this._IdleList.Count} of {IdleManager.MaxIdleGames} games  ·  ";
            text += idling == 0 && otherIdling == 0 ? "not idling" : $"{idling} idling now";
            if (otherIdling > 0)
            {
                text += $" (+{otherIdling} not on list)";
            }
            this._IdleCountLabel.Text = text;
            this._IdleCountLabel.ForeColor = this._RunningIds.Count > 0 ? Theme.GreenDot : Theme.Muted;
            this._StopIdlingButton.Enabled = this._RunningIds.Count > 0;
            this._StopIdlingButton.BackColor = this._RunningIds.Count > 0 ? Theme.Red : Theme.Surface2;
        }

        private void RefreshRunningState()
        {
            if (this._RefreshingRunning == true)
            {
                return;
            }
            this._RefreshingRunning = true;

            Task.Run(() => IdleManager.GetRunningIds()).ContinueWith(task =>
            {
                this._RefreshingRunning = false;
                if (task.Status != TaskStatus.RanToCompletion || this.IsDisposed == true)
                {
                    return;
                }

                bool changed = this._RunningIds.SetEquals(task.Result) == false;
                this._RunningIds = task.Result;
                if (changed == true)
                {
                    this._IdleListBox.Invalidate();
                    this._GameListView.Invalidate();
                }
                this.UpdateIdleCountLabel();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnIdleListBoxDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this._IdleListBox.Items.Count)
            {
                return;
            }

            var entry = (IdleEntry)this._IdleListBox.Items[e.Index];
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool isRunning = this._RunningIds.Contains(entry.Id);
            bool isInstalled = this._InstalledIds.Contains(entry.Id);
            var g = e.Graphics;

            using (var back = new SolidBrush(isSelected ? Theme.Accent : Theme.Back))
            {
                g.FillRectangle(back, e.Bounds);
            }

            int dotSize = Math.Max(8, e.Bounds.Height / 3);
            var dot = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - dotSize) / 2, dotSize, dotSize);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (isRunning == true)
            {
                using var brush = new SolidBrush(Theme.GreenDot);
                g.FillEllipse(brush, dot);
            }
            else
            {
                using var pen = new Pen(Theme.Muted);
                g.DrawEllipse(pen, dot);
            }

            string status = isInstalled ? "" : "not installed";
            int statusWidth = status.Length > 0 ? TextRenderer.MeasureText(status, Theme.SmallFont).Width + 8 : 0;

            var textBounds = new Rectangle(dot.Right + 10, e.Bounds.Top, e.Bounds.Width - dot.Right - 16 - statusWidth, e.Bounds.Height);
            TextRenderer.DrawText(g, entry.Name, Theme.UiFont, textBounds, isInstalled ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (status.Length > 0)
            {
                var statusRect = new Rectangle(e.Bounds.Right - statusWidth - 6, e.Bounds.Top, statusWidth, e.Bounds.Height);
                TextRenderer.DrawText(g, status, Theme.SmallFont, statusRect, Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
        }

        // ---------------------------------------------------------------- actions

        private void OpenIdleChooser()
        {
            var candidates = this._Games.Values
                .Select(g => new IdleGamesDialog.Candidate
                {
                    Id = g.Id,
                    Name = g.Name,
                    Installed = IdleManager.IsInstalled(this._SteamClient, g.Id),
                    Running = this._RunningIds.Contains(g.Id),
                })
                .ToList();

            // Keep games already on the list even if the current view doesn't include them.
            foreach (var id in this._IdleList.Where(id => this._Games.ContainsKey(id) == false))
            {
                candidates.Add(new IdleGamesDialog.Candidate
                {
                    Id = id,
                    Name = this.IdleGameName(id),
                    Installed = IdleManager.IsInstalled(this._SteamClient, id),
                    Running = this._RunningIds.Contains(id),
                });
            }

            using var dialog = new IdleGamesDialog(candidates, this._IdleList);
            dialog.Icon = this.Icon;
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            this._IdleList.Clear();
            this._IdleList.AddRange(dialog.SelectedIds);
            this.SaveIdleList();
            this.RefreshIdlePanel();
            this._PickerStatusLabel.Text = $"Idle list saved: {this._IdleList.Count} game(s). Click \"Start idling\" to begin.";
        }

        private List<GameInfo> GetSelectedGames()
        {
            return this._GameListView.SelectedIndices
                .Cast<int>()
                .Where(i => i >= 0 && i < this._FilteredGames.Count)
                .Select(i => this._FilteredGames[i])
                .ToList();
        }

        /// <summary>Adds games to the idle list, explaining any that were skipped.</summary>
        private void AddGamesToIdleList(IEnumerable<GameInfo> games)
        {
            int added = 0;
            List<string> notInstalled = new();
            bool full = false;

            foreach (var game in games)
            {
                if (this._IdleList.Contains(game.Id))
                {
                    continue;
                }

                if (this._IdleList.Count >= IdleManager.MaxIdleGames)
                {
                    full = true;
                    break;
                }

                if (IdleManager.IsInstalled(this._SteamClient, game.Id) == false)
                {
                    notInstalled.Add(game.Name);
                    continue;
                }

                this._IdleList.Add(game.Id);
                added++;
            }

            if (added > 0)
            {
                this.SaveIdleList();
                this.RefreshIdlePanel();
                this._PickerStatusLabel.Text = $"Added {added} game(s) to the idle list.";
            }

            if (notInstalled.Count > 0 || full == true)
            {
                string message = "";
                if (notInstalled.Count > 0)
                {
                    message += "Only installed games can idle. Not installed:\n\n  " +
                        string.Join("\n  ", notInstalled.Take(10)) +
                        (notInstalled.Count > 10 ? $"\n  ...and {notInstalled.Count - 10} more" : "") + "\n\n";
                }
                if (full == true)
                {
                    message += $"The idle list is limited to {IdleManager.MaxIdleGames} games.";
                }
                MessageBox.Show(this, message.Trim(), "Idle List", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AddSelectedGamesToIdleList()
        {
            var games = this.GetSelectedGames();
            if (games.Count == 0)
            {
                this._PickerStatusLabel.Text = "Select one or more games on the left first.";
                return;
            }
            this.AddGamesToIdleList(games);
        }

        private void RemoveSelectedIdleEntries()
        {
            var ids = this._IdleListBox.SelectedItems.Cast<IdleEntry>().Select(e => e.Id).ToList();
            if (ids.Count == 0)
            {
                return;
            }

            this._IdleList.RemoveAll(ids.Contains);
            this.SaveIdleList();
            this.RefreshIdlePanel();
        }

        private void OnClearIdleList(object sender, EventArgs e)
        {
            if (this._IdleList.Count == 0)
            {
                return;
            }

            if (MessageBox.Show(this, "Remove all games from the idle list?", "Idle List",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            this._IdleList.Clear();
            this.SaveIdleList();
            this.RefreshIdlePanel();
        }

        private void OnStartupCheckBoxChanged(object sender, EventArgs e)
        {
            if (this._UpdatingStartup == true)
            {
                return;
            }

            bool enabled = this._StartupCheckBox.Checked;
            try
            {
                IdleManager.SetStartupEnabled(enabled);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't change the Windows startup setting:\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.SetStartupUi(!enabled);
                return;
            }

            this.SetStartupUi(enabled);
            this._PickerStatusLabel.Text = enabled
                ? (this._IdleList.Count > 0
                    ? $"The {this._IdleList.Count} game(s) on your idle list will start idling when you sign in to Windows."
                    : "Startup idling is on. Choose some games so there's something to idle.")
                : "Startup idling is off.";
        }

        private void OnStartIdling(object sender, EventArgs e)
        {
            if (this._IdleList.Count == 0)
            {
                this.OpenIdleChooser();
                if (this._IdleList.Count == 0)
                {
                    return;
                }
            }

            this.RefreshIdlePanel();
            var ids = this._IdleList.Where(this._InstalledIds.Contains).ToList();
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "None of the games on your idle list are installed.",
                    "Start Idling", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this._StartIdlingButton.Enabled = false;
            this._PickerStatusLabel.Text = $"Starting {ids.Count} game(s)...";

            Task.Run(() => IdleManager.Start(ids, 300)).ContinueWith(task =>
            {
                if (this.IsDisposed == true)
                {
                    return;
                }

                this._StartIdlingButton.Enabled = true;
                int started = task.Status == TaskStatus.RanToCompletion ? task.Result : 0;
                this._PickerStatusLabel.Text = started > 0
                    ? $"Started idling {started} game(s) in the background. You can close this window."
                    : "Everything on the idle list is already idling.";
                this.RefreshRunningState();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnStopIdling(object sender, EventArgs e)
        {
            int stopped = IdleManager.StopAll();
            this._PickerStatusLabel.Text = stopped > 0
                ? $"Stopped {stopped} idling game(s)."
                : "Nothing was idling.";
            this.RefreshRunningState();
        }

        private void OnGameListViewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Insert)
            {
                this.AddSelectedGamesToIdleList();
                e.Handled = true;
            }
        }

        private void OnOpenAchievementsMenuItem(object sender, EventArgs e)
        {
            if (this._ContextMenuGame != null)
            {
                this.LaunchManager(this._ContextMenuGame);
            }
        }
    }
}
