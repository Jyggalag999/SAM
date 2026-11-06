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

// Modified: "Choose games to idle" dialog.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SAM.Picker
{
    /// <summary>Checklist of the user's games for picking what to idle.</summary>
    internal sealed class IdleGamesDialog : Form
    {
        internal sealed class Candidate
        {
            public uint Id;
            public string Name;
            public bool Installed;
            public bool Running;
        }

        private readonly List<Candidate> _All;
        private readonly List<Candidate> _Visible = new();
        private readonly List<uint> _Selected;

        private readonly TextBox _SearchBox;
        private readonly CheckBox _ShowUninstalledCheckBox;
        private readonly ListBox _List;
        private readonly Label _CountLabel;

        public IReadOnlyList<uint> SelectedIds => this._Selected;

        public IdleGamesDialog(IEnumerable<Candidate> games, IEnumerable<uint> selected)
        {
            this._All = games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            this._Selected = selected.ToList();

            this.Text = "Choose games to idle";
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(520, 600);
            this.MinimumSize = new Size(420, 420);
            this.ShowInTaskbar = false;
            this.MinimizeBox = false;
            this.KeyPreview = true;
            Theme.ApplyForm(this);

            var title = new Label
            {
                Text = "Choose games to idle",
                Font = Theme.TitleFont,
                ForeColor = Theme.Text,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2),
            };
            var subtitle = new Label
            {
                Text = $"Tick up to {IdleManager.MaxIdleGames} games. Only installed games can idle; " +
                       "Steam shows them as \"In game\" while they do.",
                ForeColor = Theme.Muted,
                AutoSize = true,
                MaximumSize = new Size(480, 0),
                Margin = new Padding(0, 0, 0, 10),
            };

            this._SearchBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleTextBox(this._SearchBox);
            Theme.SetCueBanner(this._SearchBox, "Search games...");
            this._SearchBox.TextChanged += (s, e) => this.RefreshList();

            this._ShowUninstalledCheckBox = new CheckBox
            {
                Text = "Show games that aren't installed",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
            };
            Theme.StyleCheckBox(this._ShowUninstalledCheckBox);
            this._ShowUninstalledCheckBox.CheckedChanged += (s, e) => this.RefreshList();

            this._List = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Back,
                ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 28,
                IntegralHeight = false,
                Margin = new Padding(0, 0, 0, 8),
            };
            Theme.DarkScrollBars(this._List);
            this._List.DrawItem += this.OnDrawItem;
            this._List.MouseClick += (s, e) =>
            {
                int index = this._List.IndexFromPoint(e.Location);
                if (index >= 0)
                {
                    this.Toggle(index);
                }
            };
            this._List.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && this._List.SelectedIndex >= 0)
                {
                    this.Toggle(this._List.SelectedIndex);
                    e.Handled = true;
                }
            };

            this._CountLabel = new Label
            {
                AutoSize = true,
                ForeColor = Theme.Muted,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 12, 0),
            };

            var selectAll = Theme.MakeButton("Tick all shown", Theme.Surface2, (s, e) => this.SelectAllVisible());
            var clear = Theme.MakeButton("Untick all", Theme.Surface2, (s, e) =>
            {
                this._Selected.Clear();
                this.UpdateCount();
                this._List.Invalidate();
            });

            var listButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            listButtons.Controls.AddRange(new Control[] { this._CountLabel, selectAll, clear });

            var save = Theme.MakeButton("Save", Theme.Accent, null);
            save.DialogResult = DialogResult.OK;
            save.Font = Theme.ButtonFont;
            save.MinimumSize = new Size(96, 34);
            var cancel = Theme.MakeButton("Cancel", Theme.Surface2, null);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.MinimumSize = new Size(86, 34);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 6, 0, 0),
            };
            bottom.Controls.AddRange(new Control[] { save, cancel });

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(16),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(subtitle, 0, 1);
            layout.Controls.Add(this._SearchBox, 0, 2);
            layout.Controls.Add(this._ShowUninstalledCheckBox, 0, 3);
            layout.Controls.Add(this._List, 0, 4);
            layout.Controls.Add(listButtons, 0, 5);
            layout.Controls.Add(bottom, 0, 6);
            this.Controls.Add(layout);

            this.AcceptButton = save;
            this.CancelButton = cancel;

            this.RefreshList();
            this.Shown += (s, e) => this._SearchBox.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Arrow keys in the search box move through the list.
            if (this._SearchBox.Focused && (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && this._List.Items.Count > 0)
            {
                this._List.Focus();
                if (this._List.SelectedIndex < 0)
                {
                    this._List.SelectedIndex = 0;
                }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        private void RefreshList()
        {
            var search = this._SearchBox.Text.Trim();
            bool showUninstalled = this._ShowUninstalledCheckBox.Checked;

            this._Visible.Clear();
            this._Visible.AddRange(this._All.Where(g =>
                (showUninstalled || g.Installed || this._Selected.Contains(g.Id)) &&
                (search.Length == 0 || g.Name.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)));

            this._List.BeginUpdate();
            this._List.Items.Clear();
            foreach (var game in this._Visible)
            {
                this._List.Items.Add(game.Name);
            }
            this._List.EndUpdate();
            this.UpdateCount();
        }

        private void UpdateCount()
        {
            this._CountLabel.Text = $"{this._Selected.Count} of {IdleManager.MaxIdleGames} ticked";
            this._CountLabel.ForeColor = this._Selected.Count >= IdleManager.MaxIdleGames ? Color.FromArgb(230, 170, 60) : Theme.Muted;
        }

        private void Toggle(int index)
        {
            if (index < 0 || index >= this._Visible.Count)
            {
                return;
            }

            var game = this._Visible[index];
            if (this._Selected.Contains(game.Id))
            {
                this._Selected.Remove(game.Id);
            }
            else if (game.Installed == false)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            else if (this._Selected.Count >= IdleManager.MaxIdleGames)
            {
                MessageBox.Show(this, $"You can idle up to {IdleManager.MaxIdleGames} games at once.",
                    "Choose games to idle", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            else
            {
                this._Selected.Add(game.Id);
            }

            this.UpdateCount();
            this._List.Invalidate(this._List.GetItemRectangle(index));
        }

        private void SelectAllVisible()
        {
            foreach (var game in this._Visible.Where(g => g.Installed))
            {
                if (this._Selected.Count >= IdleManager.MaxIdleGames)
                {
                    break;
                }
                if (this._Selected.Contains(game.Id) == false)
                {
                    this._Selected.Add(game.Id);
                }
            }
            this.UpdateCount();
            this._List.Invalidate();
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this._Visible.Count)
            {
                return;
            }

            var game = this._Visible[e.Index];
            bool ticked = this._Selected.Contains(game.Id);
            bool focused = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var back = new SolidBrush(focused ? Theme.Surface2 : Theme.Back))
            {
                g.FillRectangle(back, e.Bounds);
            }

            var box = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
            if (ticked)
            {
                using var fill = new SolidBrush(Theme.Accent);
                g.FillRectangle(fill, box);
                using var tick = new Pen(Color.White, 2f);
                g.DrawLines(tick, new[]
                {
                    new Point(box.Left + 3, box.Top + 8),
                    new Point(box.Left + 7, box.Top + 12),
                    new Point(box.Right - 3, box.Top + 4),
                });
            }
            else
            {
                using var pen = new Pen(game.Installed ? Theme.Muted : Theme.Border);
                g.DrawRectangle(pen, box);
            }

            string status = game.Running ? "idling" : game.Installed ? "" : "not installed";
            var statusWidth = status.Length > 0 ? TextRenderer.MeasureText(status, Theme.SmallFont).Width + 12 : 0;

            var textRect = new Rectangle(box.Right + 10, e.Bounds.Top, e.Bounds.Width - box.Right - 20 - statusWidth, e.Bounds.Height);
            TextRenderer.DrawText(g, game.Name, Theme.UiFont, textRect, game.Installed ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (status.Length > 0)
            {
                var statusRect = new Rectangle(e.Bounds.Right - statusWidth - 6, e.Bounds.Top, statusWidth, e.Bounds.Height);
                TextRenderer.DrawText(g, status, Theme.SmallFont, statusRect, game.Running ? Theme.GreenDot : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
        }
    }
}
