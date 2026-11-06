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

// Modified: shared dark theme for SAM.Picker and SAM.Game (linked into both projects).

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SAM
{
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(18, 18, 21);
        public static readonly Color Surface = Color.FromArgb(30, 30, 34);
        public static readonly Color Surface2 = Color.FromArgb(44, 44, 50);
        public static readonly Color Border = Color.FromArgb(62, 62, 70);
        public static readonly Color Text = Color.FromArgb(236, 236, 240);
        public static readonly Color Muted = Color.FromArgb(150, 150, 160);
        public static readonly Color Accent = Color.FromArgb(0, 110, 200);
        public static readonly Color AccentHover = Color.FromArgb(20, 130, 220);
        public static readonly Color Green = Color.FromArgb(46, 125, 50);
        public static readonly Color GreenDot = Color.FromArgb(76, 175, 80);
        public static readonly Color Red = Color.FromArgb(150, 40, 40);
        public static readonly Color Warning = Color.FromArgb(70, 22, 22);

        public static readonly Font UiFont = new("Segoe UI", 9F);
        public static readonly Font SmallFont = new("Segoe UI", 8.25F);
        public static readonly Font TitleFont = new("Segoe UI Semibold", 14F);
        public static readonly Font ButtonFont = new("Segoe UI Semibold", 10.5F);

        private static readonly ToolStripRenderer Renderer = new DarkRenderer();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        /// <summary>Dark window background, dark title bar, dark scroll bars on child lists.</summary>
        public static void ApplyForm(Form form)
        {
            form.BackColor = Back;
            form.ForeColor = Text;
            form.Font = UiFont;

            void OnHandle(object sender, EventArgs e)
            {
                int on = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ / 11); 19 on older builds.
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
                }
            }

            if (form.IsHandleCreated)
            {
                OnHandle(form, EventArgs.Empty);
            }
            else
            {
                form.HandleCreated += OnHandle;
            }
        }

        /// <summary>Dark scroll bars for ListView/ListBox/DataGridView (Windows 10 1809+).</summary>
        public static void DarkScrollBars(Control control)
        {
            void OnHandle(object sender, EventArgs e) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            if (control.IsHandleCreated)
            {
                OnHandle(control, EventArgs.Empty);
            }
            else
            {
                control.HandleCreated += OnHandle;
            }
        }

        public static void ApplyToolStrip(ToolStrip strip)
        {
            strip.Renderer = Renderer;
            strip.BackColor = Surface;
            strip.ForeColor = Text;
            strip.Font = UiFont;
            if (strip is not StatusStrip && strip is not MenuStrip)
            {
                strip.Padding = new Padding(6, 3, 6, 3);
                strip.GripStyle = ToolStripGripStyle.Hidden;
            }

            foreach (ToolStripItem item in strip.Items)
            {
                ApplyItem(item);
            }
        }

        private static void ApplyItem(ToolStripItem item)
        {
            item.ForeColor = Text;
            if (item is ToolStripTextBox textBox)
            {
                textBox.BackColor = Surface2;
                textBox.ForeColor = Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (item is ToolStripDropDownItem dropDown)
            {
                if (dropDown.DropDown != null)
                {
                    dropDown.DropDown.Renderer = Renderer;
                    dropDown.DropDown.BackColor = Surface;
                }
                foreach (ToolStripItem child in dropDown.DropDownItems)
                {
                    ApplyItem(child);
                }
            }
        }

        public static void ApplyContextMenu(ContextMenuStrip menu)
        {
            menu.Renderer = Renderer;
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            foreach (ToolStripItem item in menu.Items)
            {
                ApplyItem(item);
            }
        }

        /// <summary>Grey placeholder text inside an empty text box.</summary>
        public static void SetCueBanner(TextBoxBase textBox, string text)
        {
            const int EM_SETCUEBANNER = 0x1501;
            void OnHandle(object sender, EventArgs e) => SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
            if (textBox.IsHandleCreated)
            {
                OnHandle(textBox, EventArgs.Empty);
            }
            else
            {
                textBox.HandleCreated += OnHandle;
            }
        }

        public static Button MakeButton(string text, Color back, EventHandler onClick)
        {
            Button button = new()
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font = UiFont,
                Margin = new Padding(0, 0, 6, 6),
                Padding = new Padding(8, 3, 8, 3),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            button.FlatAppearance.BorderColor = ControlPaint.Light(back, 0.25f);
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.2f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.05f);
            if (onClick != null)
            {
                button.Click += onClick;
            }
            return button;
        }

        public static void StyleCheckBox(CheckBox checkBox)
        {
            checkBox.ForeColor = Text;
            checkBox.BackColor = Color.Transparent;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.FlatAppearance.BorderColor = Border;
            checkBox.FlatAppearance.CheckedBackColor = Accent;
        }

        public static void StyleTextBox(TextBox textBox)
        {
            textBox.BackColor = Surface2;
            textBox.ForeColor = Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Font = new Font("Segoe UI", 10F);
        }

        /// <summary>Dark column headers for a Details-view ListView; rows draw normally.</summary>
        public static void DarkHeaders(ListView listView)
        {
            listView.OwnerDraw = true;
            listView.DrawColumnHeader += (s, e) =>
            {
                using (var back = new SolidBrush(Surface2))
                {
                    e.Graphics.FillRectangle(back, e.Bounds);
                    if (e.ColumnIndex == listView.Columns.Count - 1)
                    {
                        // Also paint the empty header area to the right of the last column.
                        e.Graphics.FillRectangle(back, new Rectangle(e.Bounds.Right, e.Bounds.Top, 4000, e.Bounds.Height));
                    }
                }
                using (var pen = new Pen(Border))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                }
                var text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, UiFont, text, Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            listView.DrawItem += (s, e) => e.DrawDefault = true;
            listView.DrawSubItem += (s, e) => e.DrawDefault = true;
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = Back;
            grid.GridColor = Border;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersVisible = false;

            grid.DefaultCellStyle.BackColor = Back;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = Accent;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Font = UiFont;

            grid.ColumnHeadersDefaultCellStyle.BackColor = Surface2;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface2;
            grid.ColumnHeadersDefaultCellStyle.Font = UiFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4);
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => Surface;
            public override Color ToolStripGradientMiddle => Surface;
            public override Color ToolStripGradientEnd => Surface;
            public override Color ToolStripBorder => Surface;
            public override Color ToolStripDropDownBackground => Surface;
            public override Color MenuStripGradientBegin => Surface;
            public override Color MenuStripGradientEnd => Surface;
            public override Color StatusStripGradientBegin => Surface;
            public override Color StatusStripGradientEnd => Surface;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Accent;
            public override Color MenuItemSelected => Surface2;
            public override Color MenuItemSelectedGradientBegin => Surface2;
            public override Color MenuItemSelectedGradientEnd => Surface2;
            public override Color MenuItemPressedGradientBegin => Surface2;
            public override Color MenuItemPressedGradientMiddle => Surface2;
            public override Color MenuItemPressedGradientEnd => Surface2;
            public override Color ImageMarginGradientBegin => Surface;
            public override Color ImageMarginGradientMiddle => Surface;
            public override Color ImageMarginGradientEnd => Surface;
            public override Color ButtonSelectedHighlight => Surface2;
            public override Color ButtonSelectedHighlightBorder => Border;
            public override Color ButtonSelectedBorder => Border;
            public override Color ButtonSelectedGradientBegin => Surface2;
            public override Color ButtonSelectedGradientMiddle => Surface2;
            public override Color ButtonSelectedGradientEnd => Surface2;
            public override Color ButtonPressedGradientBegin => Border;
            public override Color ButtonPressedGradientMiddle => Border;
            public override Color ButtonPressedGradientEnd => Border;
            public override Color ButtonCheckedGradientBegin => Accent;
            public override Color ButtonCheckedGradientMiddle => Accent;
            public override Color ButtonCheckedGradientEnd => Accent;
            public override Color ButtonCheckedHighlight => Accent;
            public override Color ButtonCheckedHighlightBorder => Accent;
            public override Color CheckBackground => Accent;
            public override Color CheckSelectedBackground => AccentHover;
            public override Color CheckPressedBackground => Accent;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Surface;
            public override Color GripDark => Border;
            public override Color GripLight => Surface;
        }

        private sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors())
            {
                this.RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Text : Muted;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Muted;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (e.ToolStrip is ToolStripDropDown)
                {
                    using var pen = new Pen(Border);
                    e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
                }
                // Top-level strips: no border line.
            }
        }
    }
}
