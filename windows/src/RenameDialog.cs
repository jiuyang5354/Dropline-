using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Dropline
{
    // Small dialog for batch rename: a name pattern, a start number and a live preview.
    internal sealed class RenameDialog : Window
    {
        private readonly IList<string> paths;
        private readonly TextBox pattern;
        private readonly TextBox start;
        private readonly TextBlock preview;

        public RenameDialog(IList<string> paths, string lastPattern)
        {
            this.paths = paths;
            Title = "批量重命名";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var sans = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
            var mono = new FontFamily("Cascadia Mono, Consolas");
            var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x14));
            var muted = new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0x7E));
            var hairline = new SolidColorBrush(Color.FromArgb(0x26, 0x16, 0x16, 0x14));
            var accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x1F));

            var stack = new StackPanel { Width = 300 };
            stack.Children.Add(new TextBlock { Text = "批量重命名 · " + paths.Count + " 个", FontFamily = mono, FontSize = 11, Foreground = ink, Margin = new Thickness(0, 0, 0, 12) });

            stack.Children.Add(new TextBlock { Text = "名称规则", FontFamily = sans, FontSize = 11, Foreground = muted });
            pattern = new TextBox { Text = string.IsNullOrEmpty(lastPattern) ? "{name}-{n}" : lastPattern, FontFamily = mono, FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 4, 0, 4), BorderBrush = hairline };
            stack.Children.Add(pattern);
            stack.Children.Add(new TextBlock { Text = "{n} 序号　{name} 原名　{date} 今天日期", FontFamily = sans, FontSize = 11, Foreground = muted, Margin = new Thickness(0, 0, 0, 10) });

            var startRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            startRow.Children.Add(new TextBlock { Text = "起始序号", FontFamily = sans, FontSize = 11, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            start = new TextBox { Text = "1", Width = 56, FontFamily = mono, FontSize = 13, Padding = new Thickness(6, 2, 6, 2), BorderBrush = hairline, HorizontalAlignment = HorizontalAlignment.Left };
            startRow.Children.Add(start);
            stack.Children.Add(startRow);

            preview = new TextBlock { FontFamily = mono, FontSize = 11, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 14) };
            stack.Children.Add(preview);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), IsCancel = true, FontFamily = sans, Background = Brushes.Transparent, BorderBrush = hairline };
            var ok = new Button { Content = "生成副本", Padding = new Thickness(14, 4, 14, 4), IsDefault = true, FontFamily = sans, Background = accent, Foreground = Brushes.White, BorderBrush = accent };
            ok.Click += delegate { if (StartNumber >= 0) DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            stack.Children.Add(buttons);

            Content = new Border
            {
                Margin = new Thickness(16),
                Padding = new Thickness(18, 16, 18, 16),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF4, 0xEF)),
                BorderBrush = hairline,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.22 },
                Child = stack
            };

            pattern.TextChanged += delegate { UpdatePreview(); };
            start.TextChanged += delegate { UpdatePreview(); };
            MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == Content || e.OriginalSource is Border) DragMove(); };
            Loaded += delegate { pattern.Focus(); pattern.SelectAll(); Activate(); };
            UpdatePreview();
        }

        public string Pattern { get { return pattern.Text; } }

        public int StartNumber
        {
            get
            {
                int n;
                return int.TryParse(start.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 ? n : -1;
            }
        }

        private void UpdatePreview()
        {
            int n = StartNumber;
            if (n < 0) { preview.Text = "起始序号需要是数字"; return; }
            var lines = new List<string>();
            for (int i = 0; i < Math.Min(3, paths.Count); i++)
                lines.Add(Processor.RenameTarget(Pattern, paths[i], i, paths.Count, n));
            if (paths.Count > 3) lines.Add("…");
            preview.Text = string.Join("\n", lines);
        }
    }
}
