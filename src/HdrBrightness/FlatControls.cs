using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HdrBrightness
{
    /// <summary>扁平卡片：圆角白底，可选细边框。</summary>
    internal class FlatCard : Panel
    {
        public FlatCard()
        {
            BackColor = FlatTheme.PageBackground;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public Color SurfaceColor { get; set; } = FlatTheme.Surface;
        public Color BorderColor { get; set; } = FlatTheme.Border;
        public int CornerRadius { get; set; } = 12;

        protected int UiScale(int pixels)
        {
            return FlatTheme.Scale(this, pixels);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = UiScale(CornerRadius);
            FlatTheme.FillRounded(e.Graphics, bounds, radius, SurfaceColor);
            FlatTheme.DrawRounded(e.Graphics, bounds, radius, BorderColor, 1f);
        }
    }

    /// <summary>分区小标题：左侧一个主色小竖条 + 文字。</summary>
    internal sealed class SectionHeader : Control
    {
        public SectionHeader(string text)
        {
            Text = text;
            Font = FlatTheme.Section;
            ForeColor = FlatTheme.TextSecondary;
            BackColor = FlatTheme.PageBackground;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            int barWidth = FlatTheme.Scale(this, 3);
            int barHeight = FlatTheme.Scale(this, 12);
            var bar = new Rectangle(0, (Height - barHeight) / 2, barWidth, barHeight);
            FlatTheme.FillRounded(e.Graphics, bar, barWidth / 2, FlatTheme.Accent);

            var textRect = new Rectangle(bar.Right + FlatTheme.Scale(this, 8), 0, Width - bar.Right, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>扁平滑块：细轨道 + 实心进度 + 白色圆点滑块。</summary>
    internal sealed class FlatSlider : Control
    {
        private int _value;
        private bool _dragging;
        private bool _hover;

        public FlatSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = FlatTheme.Surface;
            Height = FlatTheme.Scale(this, 26);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        /// <summary>值变化（拖动过程中连续触发）。</summary>
        public Action<int> ValueChanged { get; set; }

        /// <summary>一次拖动 / 键盘调整结束。</summary>
        public Action DragCompleted { get; set; }

        public int Minimum { get { return 0; } }
        public int Maximum { get { return 100; } }
        public bool IsDragging { get { return _dragging; } }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(Minimum, Math.Min(Maximum, value));
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
                if (ValueChanged != null) ValueChanged(_value);
            }
        }

        private int ThumbRadius { get { return FlatTheme.Scale(this, 8); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            int radius = ThumbRadius;
            int trackHeight = FlatTheme.Scale(this, 6);
            int left = radius;
            int right = Width - radius;
            if (right <= left) return;

            int trackTop = (Height - trackHeight) / 2;
            var track = new Rectangle(left, trackTop, right - left, trackHeight);
            FlatTheme.FillRounded(e.Graphics, track, trackHeight / 2, FlatTheme.SurfaceHover);

            double ratio = (double)(_value - Minimum) / (Maximum - Minimum);
            int thumbX = left + (int)Math.Round(ratio * (right - left));

            var filled = new Rectangle(left, trackTop, Math.Max(trackHeight, thumbX - left), trackHeight);
            FlatTheme.FillRounded(e.Graphics, filled, trackHeight / 2, FlatTheme.Accent);

            int thumbRadius = _dragging || _hover ? radius + FlatTheme.Scale(this, 1) : radius;
            var thumb = new Rectangle(thumbX - thumbRadius, (Height / 2) - thumbRadius, thumbRadius * 2, thumbRadius * 2);

            if (_dragging)
            {
                var glowRadius = radius + FlatTheme.Scale(this, 4);
                var glow = new Rectangle(thumbX - glowRadius, (Height / 2) - glowRadius, glowRadius * 2, glowRadius * 2);
                FlatTheme.FillRounded(e.Graphics, glow, glowRadius, Color.FromArgb(38, FlatTheme.Accent));
            }

            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(brush, thumb);
            }
            using (var pen = new Pen(FlatTheme.Accent, FlatTheme.Scale(this, 2)))
            {
                e.Graphics.DrawEllipse(pen, thumb);
            }

            if (Focused && !_dragging)
            {
                var ring = new Rectangle(thumb.X - 3, thumb.Y - 3, thumb.Width + 5, thumb.Height + 5);
                using (var pen = new Pen(Color.FromArgb(70, FlatTheme.Accent), 1f))
                {
                    e.Graphics.DrawEllipse(pen, ring);
                }
            }
        }

        private void SetFromPosition(int x)
        {
            int radius = ThumbRadius;
            int left = radius;
            int right = Width - radius;
            if (right <= left) return;

            double ratio = (double)(x - left) / (right - left);
            Value = (int)Math.Round(ratio * (Maximum - Minimum)) + Minimum;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Focus();
            _dragging = true;
            SetFromPosition(e.X);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_hover)
            {
                _hover = true;
                Invalidate();
            }

            if (_dragging) SetFromPosition(e.X);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover && !_dragging)
            {
                _hover = false;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;

            _dragging = false;
            Invalidate();
            if (DragCompleted != null) DragCompleted();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            int step = e.Shift ? 1 : 5;
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value = _value - step;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value = _value + step;
                    break;
                case Keys.PageDown:
                    Value = _value - 10;
                    break;
                case Keys.PageUp:
                    Value = _value + 10;
                    break;
                case Keys.Home:
                    Value = Minimum;
                    break;
                case Keys.End:
                    Value = Maximum;
                    break;
                default:
                    return;
            }

            e.Handled = true;
            if (DragCompleted != null) DragCompleted();
        }
    }

    internal enum FlatButtonKind
    {
        Plain,
        Subtle,
        Primary,
    }

    /// <summary>扁平按钮：无边框、圆角、悬停变色。</summary>
    internal sealed class FlatButton : Control, IButtonControl
    {
        private bool _hover;
        private bool _pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = FlatTheme.Body;
            BackColor = FlatTheme.Surface;
            Cursor = Cursors.Hand;
            TabStop = true;
            Kind = FlatButtonKind.Plain;
            CornerRadius = 7;
        }

        public FlatButtonKind Kind { get; set; }
        public int CornerRadius { get; set; }
        public Action Clicked { get; set; }

        // IButtonControl：这样它可以当 Form 的 AcceptButton / CancelButton
        private DialogResult _dialogResult = DialogResult.None;

        public DialogResult DialogResult
        {
            get { return _dialogResult; }
            set { _dialogResult = value; }
        }

        public void NotifyDefault(bool value)
        {
        }

        public void PerformClick()
        {
            RaiseClick();
        }

        private void RaiseClick()
        {
            if (Clicked != null) Clicked();

            if (_dialogResult != DialogResult.None)
            {
                var form = FindForm();
                if (form != null) form.DialogResult = _dialogResult;
                else
                {
                    var parent = Parent;
                    while (parent != null && !(parent is Form)) parent = parent.Parent;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = FlatTheme.Scale(this, CornerRadius);
            Color background = Color.Empty;
            Color foreground;

            switch (Kind)
            {
                case FlatButtonKind.Primary:
                    background = _pressed ? Darken(FlatTheme.Accent, 0.10f)
                               : _hover ? Darken(FlatTheme.Accent, 0.06f)
                               : FlatTheme.Accent;
                    foreground = Color.White;
                    break;
                case FlatButtonKind.Subtle:
                    background = _pressed ? FlatTheme.SurfaceHover
                               : _hover ? FlatTheme.SurfaceHover
                               : FlatTheme.SurfaceSubtle;
                    foreground = FlatTheme.TextPrimary;
                    break;
                default:
                    if (_pressed || _hover) background = FlatTheme.SurfaceHover;
                    foreground = _hover ? FlatTheme.TextPrimary : FlatTheme.TextSecondary;
                    break;
            }

            if (background != Color.Empty)
            {
                FlatTheme.FillRounded(e.Graphics, bounds, radius, background);
            }

            if (Focused)
            {
                FlatTheme.DrawRounded(e.Graphics, bounds, radius, Color.FromArgb(90, FlatTheme.Accent), 1f);
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static Color Darken(Color color, float amount)
        {
            return Color.FromArgb(
                color.A,
                (int)(color.R * (1 - amount)),
                (int)(color.G * (1 - amount)),
                (int)(color.B * (1 - amount)));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasPressed = _pressed;
            _pressed = false;
            Invalidate();

            if (wasPressed && ClientRectangle.Contains(e.Location))
            {
                RaiseClick();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                RaiseClick();
            }
        }
    }

    /// <summary>小圆角标签（HDR 状态、分组标记等）。</summary>
    internal sealed class FlatBadge : Control
    {
        public FlatBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = FlatTheme.Small;
            BackColor = FlatTheme.Surface;
        }

        public Color BadgeColor { get; set; } = FlatTheme.AccentSoft;
        public Color BadgeTextColor { get; set; } = FlatTheme.AccentText;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            FlatTheme.FillRounded(e.Graphics, bounds, Height / 2, BadgeColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, BadgeTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>分段式常用亮度选择器：0 / 20 / 50 / 75 / 100。</summary>
    internal sealed class FlatSegmented : Control
    {
        public static readonly int[] Presets = new int[] { 0, 20, 50, 75, 100 };

        private int? _selected;
        private int _hotIndex = -1;
        private readonly Action<int> _onPick;

        public FlatSegmented(Action<int> onPick)
        {
            _onPick = onPick;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = FlatTheme.Small;
            BackColor = FlatTheme.Surface;
            Cursor = Cursors.Hand;
        }

        public void SetSelected(int? percent)
        {
            if (_selected == percent) return;
            _selected = percent;
            Invalidate();
        }

        private int ItemWidth
        {
            get { return Math.Max(1, Width / Presets.Length); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            int inset = FlatTheme.Scale(this, 2);
            var track = new Rectangle(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
            int radius = FlatTheme.Scale(this, 7);
            FlatTheme.FillRounded(e.Graphics, track, radius, FlatTheme.SurfaceSubtle);

            int itemWidth = ItemWidth;
            for (int i = 0; i < Presets.Length; i++)
            {
                var item = new Rectangle(i * itemWidth, 0, itemWidth, Height);
                bool active = _selected.HasValue && _selected.Value == Presets[i];

                if (active)
                {
                    var pill = new Rectangle(item.X + inset, inset, item.Width - inset * 2, item.Height - inset * 2 - 1);
                    FlatTheme.FillRounded(e.Graphics, pill, radius, FlatTheme.Surface);
                    FlatTheme.DrawRounded(e.Graphics, pill, radius, FlatTheme.BorderStrong, 1f);
                }
                else if (i == _hotIndex)
                {
                    var pill = new Rectangle(item.X + inset, inset, item.Width - inset * 2, item.Height - inset * 2 - 1);
                    FlatTheme.FillRounded(e.Graphics, pill, radius, FlatTheme.SurfaceHover);
                }

                Color color = active ? FlatTheme.AccentText : FlatTheme.TextSecondary;
                Font font = active ? FlatTheme.SmallBold : FlatTheme.Small;
                TextRenderer.DrawText(e.Graphics, Presets[i].ToString(), font, item, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private int IndexFromX(int x)
        {
            int index = x / ItemWidth;
            if (index < 0) index = 0;
            if (index >= Presets.Length) index = Presets.Length - 1;
            return index;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexFromX(e.X);
            if (index != _hotIndex)
            {
                _hotIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hotIndex = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int percent = Presets[IndexFromX(e.X)];
            SetSelected(percent);
            if (_onPick != null) _onPick(percent);
        }
    }

    /// <summary>分组下拉：扁平按钮 + 扁平菜单。</summary>
    internal sealed class FlatDropdown : Control
    {
        private readonly List<GroupChoice> _items = new List<GroupChoice>();
        private GroupChoice _selected;
        private bool _hover;
        private bool _open;

        public FlatDropdown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = FlatTheme.Small;
            BackColor = FlatTheme.Surface;
            Cursor = Cursors.Hand;
        }

        public Action<GroupChoice> SelectionChanged { get; set; }

        public GroupChoice Selected { get { return _selected; } }

        public void SetItems(IEnumerable<GroupChoice> items, string selectedId)
        {
            _items.Clear();
            _items.AddRange(items);
            _selected = _items.Count > 0 ? _items[0] : null;
            foreach (var item in _items)
            {
                if (item.Id == selectedId) { _selected = item; break; }
            }

            if (_items.Count == 0)
            {
                _items.Add(new GroupChoice(null, "（不分组）"));
                _selected = _items[0];
            }
            Invalidate();
        }

        private string DisplayText
        {
            get
            {
                if (_selected == null) return "（不分组）";
                string text = _selected.Name;
                return text.Length > 10 ? text.Substring(0, 9) + "…" : text;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = FlatTheme.Scale(this, 7);
            FlatTheme.FillRounded(e.Graphics, bounds, radius,
                _hover || _open ? FlatTheme.SurfaceHover : FlatTheme.SurfaceSubtle);

            var textRect = new Rectangle(FlatTheme.Scale(this, 10), 0,
                Width - FlatTheme.Scale(this, 26), Height);
            TextRenderer.DrawText(e.Graphics, DisplayText, Font, textRect, FlatTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            var arrowRect = new Rectangle(Width - FlatTheme.Scale(this, 20), 0, FlatTheme.Scale(this, 12), Height);
            TextRenderer.DrawText(e.Graphics, "▾", Font, arrowRect, FlatTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            ShowMenu();
        }

        private void ShowMenu()
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                Font = FlatTheme.Body,
                BackColor = FlatTheme.Surface,
                Renderer = new FlatMenuRenderer(),
                Padding = new Padding(FlatTheme.Scale(this, 4)),
            };

            foreach (var item in _items)
            {
                var entry = new ToolStripMenuItem(item.Name);
                entry.ForeColor = item == _selected ? FlatTheme.AccentText : FlatTheme.TextPrimary;
                var captured = item;
                entry.Click += delegate
                {
                    _selected = captured;
                    Invalidate();

                    // 延后回调：选中分组会重建整列卡片，不能在菜单还在关闭时就把自己销毁
                    var callback = SelectionChanged;
                    if (callback == null) return;

                    var form = FindForm();
                    if (form != null && !form.IsDisposed)
                    {
                        form.BeginInvoke((MethodInvoker)delegate { callback(captured); });
                    }
                    else
                    {
                        callback(captured);
                    }
                };
                menu.Items.Add(entry);
            }

            _open = true;
            Invalidate();
            menu.Closed += delegate
            {
                if (!IsDisposed)
                {
                    _open = false;
                    Invalidate();
                }
            };
            menu.Show(this, new Point(0, Height + FlatTheme.Scale(this, 4)));
        }
    }

    /// <summary>扁平复选框。</summary>
    internal sealed class FlatCheckBox : Control
    {
        private bool _checked;
        private bool _hover;

        public FlatCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = FlatTheme.Body;
            BackColor = FlatTheme.PageBackground;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public Action CheckedChanged { get; set; }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);

            int box = FlatTheme.Scale(this, 17);
            int top = (Height - box) / 2;
            var boxRect = new Rectangle(0, top, box, box);
            int radius = FlatTheme.Scale(this, 5);

            if (_checked)
            {
                FlatTheme.FillRounded(e.Graphics, boxRect, radius, FlatTheme.Accent);
                using (var pen = new Pen(Color.White, FlatTheme.Scale(this, 2)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new Point(boxRect.X + box / 4, boxRect.Y + box / 2),
                        new Point(boxRect.X + box / 2 - 1, boxRect.Bottom - box / 4),
                        new Point(boxRect.Right - box / 4, boxRect.Y + box / 4),
                    });
                }
            }
            else
            {
                FlatTheme.FillRounded(e.Graphics, boxRect, radius, FlatTheme.Surface);
                FlatTheme.DrawRounded(e.Graphics, boxRect, radius,
                    _hover ? FlatTheme.TextMuted : FlatTheme.BorderStrong, 1f);
            }

            var textRect = new Rectangle(boxRect.Right + FlatTheme.Scale(this, 9), 0,
                Width - boxRect.Right - FlatTheme.Scale(this, 9), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, FlatTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                Checked = !Checked;
            }
        }
    }

    internal sealed class FlatMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return FlatTheme.Surface; } }
        public override Color MenuBorder { get { return FlatTheme.BorderStrong; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return FlatTheme.SurfaceSubtle; } }
        public override Color MenuItemSelectedGradientBegin { get { return FlatTheme.SurfaceSubtle; } }
        public override Color MenuItemSelectedGradientEnd { get { return FlatTheme.SurfaceSubtle; } }
        public override Color MenuItemPressedGradientBegin { get { return FlatTheme.SurfaceSubtle; } }
        public override Color MenuItemPressedGradientEnd { get { return FlatTheme.SurfaceSubtle; } }
        public override Color ImageMarginGradientBegin { get { return FlatTheme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return FlatTheme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return FlatTheme.Surface; } }
        public override Color SeparatorDark { get { return FlatTheme.Border; } }
        public override Color SeparatorLight { get { return FlatTheme.Border; } }
        public override Color ToolStripBorder { get { return FlatTheme.BorderStrong; } }
    }

    /// <summary>扁平菜单渲染器（托盘菜单、下拉菜单共用）。</summary>
    internal sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
    {
        public FlatMenuRenderer() : base(new FlatMenuColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? FlatTheme.TextPrimary : FlatTheme.TextMuted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(FlatTheme.Surface);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(FlatTheme.BorderStrong))
            {
                var bounds = e.AffectedBounds;
                e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            }
        }
    }

    /// <summary>分组下拉里的一项。</summary>
    internal sealed class GroupChoice
    {
        public GroupChoice(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
    }

    /// <summary>极简输入框对话框。</summary>
    internal sealed class PromptForm : Form
    {
        private readonly TextBox _textBox;

        private PromptForm(string title, string hint, string initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = FlatTheme.Body;
            BackColor = FlatTheme.Surface;
            ClientSize = new Size(380, 158);

            var titleLabel = new Label
            {
                Text = title,
                Font = FlatTheme.CardTitle,
                ForeColor = FlatTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(20, 18),
            };

            var hintLabel = new Label
            {
                Text = hint,
                Font = FlatTheme.Small,
                ForeColor = FlatTheme.TextMuted,
                AutoSize = true,
                Location = new Point(20, 44),
            };

            var box = new Panel
            {
                Location = new Point(20, 68),
                Size = new Size(340, 34),
                BackColor = FlatTheme.Surface,
                Padding = new Padding(8, 6, 8, 6),
            };
            box.Paint += delegate (object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                FlatTheme.DrawRounded(e.Graphics, new Rectangle(0, 0, box.Width - 1, box.Height - 1), 8, FlatTheme.BorderStrong, 1f);
            };

            _textBox = new TextBox
            {
                Text = initial ?? "",
                Font = FlatTheme.Body,
                BorderStyle = BorderStyle.None,
                BackColor = FlatTheme.Surface,
                Dock = DockStyle.Fill,
            };
            box.Controls.Add(_textBox);

            var okButton = new FlatButton
            {
                Text = "确定",
                Kind = FlatButtonKind.Primary,
                Font = FlatTheme.Body,
                Size = new Size(84, 32),
                Location = new Point(188, 112),
                Clicked = delegate { DialogResult = DialogResult.OK; },
            };
            var cancelButton = new FlatButton
            {
                Text = "取消",
                Kind = FlatButtonKind.Subtle,
                Font = FlatTheme.Body,
                Size = new Size(84, 32),
                Location = new Point(280, 112),
                Clicked = delegate { DialogResult = DialogResult.Cancel; },
            };

            Controls.AddRange(new Control[] { titleLabel, hintLabel, box, okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public static string Ask(IWin32Window owner, string title, string hint, string initial)
        {
            using (var form = new PromptForm(title, hint, initial))
            {
                if (form.ShowDialog(owner) == DialogResult.OK)
                {
                    return form._textBox.Text;
                }
            }
            return null;
        }
    }

    /// <summary>读不到显示器信息时显示的提示卡片。</summary>
    internal sealed class WarningCard : FlatCard
    {
        private readonly Label _titleLabel;
        private readonly Label _bodyLabel;
        private readonly FlatButton _copyButton;
        private readonly FlatButton _retryButton;

        public WarningCard(string title, string body, Action retry)
        {
            SurfaceColor = FlatTheme.AmberSoft;
            BorderColor = Color.FromArgb(0xF0, 0xDE, 0xC0);
            Height = UiScale(128);

            _titleLabel = new Label
            {
                Text = title,
                Font = FlatTheme.BodyBold,
                ForeColor = FlatTheme.Amber,
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(UiScale(20), UiScale(16)),
            };

            _bodyLabel = new Label
            {
                Text = body,
                Font = FlatTheme.Small,
                ForeColor = FlatTheme.Amber,
                AutoSize = false,
                BackColor = Color.Transparent,
                Location = new Point(UiScale(20), UiScale(44)),
            };

            _copyButton = new FlatButton
            {
                Text = "复制诊断信息",
                Kind = FlatButtonKind.Subtle,
                Font = FlatTheme.Small,
                BackColor = FlatTheme.AmberSoft,
                Clicked = CopyDiagnostics,
            };

            _retryButton = new FlatButton
            {
                Text = "重新检测",
                Kind = FlatButtonKind.Primary,
                Font = FlatTheme.Small,
                BackColor = FlatTheme.AmberSoft,
                Clicked = retry,
            };

            Controls.AddRange(new Control[] { _titleLabel, _bodyLabel, _copyButton, _retryButton });
        }

        private void CopyDiagnostics()
        {
            try
            {
                Clipboard.SetText(Diagnostics.Collect());
                MessageBox.Show(FindForm(), "诊断信息已复制到剪贴板。", "HDR 内容亮度",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "复制失败：" + ex.Message, "HDR 内容亮度",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_bodyLabel == null) return;

            int pad = UiScale(20);
            _bodyLabel.SetBounds(pad, UiScale(44), Math.Max(UiScale(160), Width - pad * 2), UiScale(46));
            _retryButton.Size = new Size(UiScale(88), UiScale(30));
            _retryButton.Location = new Point(Width - pad - _retryButton.Width, UiScale(88));
            _copyButton.Size = new Size(UiScale(112), UiScale(30));
            _copyButton.Location = new Point(_retryButton.Left - UiScale(8) - _copyButton.Width, UiScale(88));
        }
    }
}
