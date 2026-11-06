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

// Modified: dark theme for the achievement manager window.

using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SAM.Game
{
    internal partial class Manager
    {
        private Panel _AchievementsPanel;
        private Panel _StatisticsPanel;
        private ToolStripButton _AchievementsViewButton;
        private ToolStripButton _StatisticsViewButton;

        /// <summary>
        /// Swaps the light tab control for two dark panels switched from the toolbar,
        /// and themes every strip, list and grid. Called after InitializeComponent.
        /// </summary>
        private void ApplyTheme()
        {
            this.SuspendLayout();
            Theme.ApplyForm(this);
            float scale = this.DeviceDpi / 96f; // runs after the designer's auto-scaling
            this.ClientSize = new Size((int)(900 * scale), (int)(600 * scale));
            this.MinimumSize = new Size((int)(640 * scale), (int)(420 * scale));

            this._AchievementsPanel = MovePageToPanel(this._AchievementsTabPage);
            this._StatisticsPanel = MovePageToPanel(this._StatisticsTabPage);
            this._StatisticsPanel.Padding = new Padding(8);
            this._StatisticsPanel.Visible = false;

            this.Controls.Remove(this._MainTabControl);
            this.Controls.Add(this._StatisticsPanel);
            this.Controls.Add(this._AchievementsPanel);
            this._StatisticsPanel.BringToFront();
            this._AchievementsPanel.BringToFront(); // fill after the top/bottom strips dock

            this._AchievementsViewButton = new ToolStripButton("Achievements") { Checked = true };
            this._StatisticsViewButton = new ToolStripButton("Statistics");
            this._AchievementsViewButton.Click += (s, e) => this.ShowView(achievements: true);
            this._StatisticsViewButton.Click += (s, e) => this.ShowView(achievements: false);
            this._MainToolStrip.Items.Insert(0, this._StatisticsViewButton);
            this._MainToolStrip.Items.Insert(0, this._AchievementsViewButton);

            Theme.ApplyToolStrip(this._MainToolStrip);
            Theme.ApplyToolStrip(this._AchievementsToolStrip);
            Theme.ApplyToolStrip(this._MainStatusStrip);
            this._AchievementsToolStrip.BackColor = Theme.Back;
            this._MatchingStringTextBox.AutoSize = false;
            this._MatchingStringTextBox.Width = 220;
            Theme.SetCueBanner(this._MatchingStringTextBox.TextBox, "Search achievements...");
            this._MatchingStringLabel.Visible = false;

            var list = this._AchievementListView;
            list.BackColor = Theme.Back;
            list.ForeColor = Theme.Text;
            list.BorderStyle = BorderStyle.None;
            list.GridLines = false;
            Theme.DarkHeaders(list);
            Theme.DarkScrollBars(list);

            // Stretch the description column so the header has no unpainted (white) gap.
            void StretchDescription()
            {
                int others = this._AchievementNameColumnHeader.Width + this._AchievementUnlockTimeColumnHeader.Width;
                int width = list.ClientSize.Width - others - 4;
                if (width > 120)
                {
                    this._AchievementDescriptionColumnHeader.Width = width;
                }
            }
            list.ClientSizeChanged += (s, e) => StretchDescription();
            this.Shown += (s, e) => StretchDescription();

            Theme.StyleGrid(this._StatisticsDataGridView);
            this._StatisticsDataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            Theme.DarkScrollBars(this._StatisticsDataGridView);
            Theme.StyleCheckBox(this._EnableStatsEditingCheckBox);

            this.ResumeLayout(true);
        }

        private static Panel MovePageToPanel(TabPage page)
        {
            // Size the panel to the designed page (children + 6px margin) so anchored
            // children keep their margins when it is docked and resized.
            var children = page.Controls.Cast<Control>().ToArray();
            var size = new Size(children.Max(c => c.Right) + 6, children.Max(c => c.Bottom) + 6);
            var panel = new Panel { Size = size, BackColor = Theme.Back, ForeColor = Theme.Text };
            foreach (var control in children)
            {
                panel.Controls.Add(control);
            }
            panel.Dock = DockStyle.Fill;
            return panel;
        }

        private void ShowView(bool achievements)
        {
            this._AchievementsViewButton.Checked = achievements;
            this._StatisticsViewButton.Checked = !achievements;
            this._AchievementsPanel.Visible = achievements;
            this._StatisticsPanel.Visible = !achievements;
        }
    }
}
