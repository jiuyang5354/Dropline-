using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Dropline
{
    internal enum ShelfState { Hidden, Tab, Open }

    // The shelf: a narrow panel docked to the right edge of the screen.
    // Hidden when empty, a small tab when it holds something, and fully open while a drag is
    // under way or the pointer rests on it.
    internal sealed class ShelfWindow : Window
    {
        private const double OpenWidth = 156;
        private const double TabWidth = 22;
        private const double TileSize = 104;
        private const double Margin_ = 10;

        private static readonly Brush Paper = Frozen(Color.FromArgb(0xF2, 0xF6, 0xF4, 0xEF));
        private static readonly Brush Ink = Frozen(Color.FromRgb(0x16, 0x16, 0x14));
        private static readonly Brush Muted = Frozen(Color.FromRgb(0x8A, 0x86, 0x7E));
        private static readonly Brush Hairline = Frozen(Color.FromArgb(0x26, 0x16, 0x16, 0x14));
        private static readonly Brush Accent = Frozen(Color.FromRgb(0xFF, 0x5A, 0x1F));
        private static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");
        private static readonly FontFamily Sans = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");

        private readonly ShelfStore store;
        private readonly Border card;
        private readonly Border tab;
        private readonly TextBlock tabCount;
        private readonly StackPanel tiles;
        private readonly ScrollViewer scroller;
        private readonly Border dropHint;
        private readonly TextBlock headerCount;
        private readonly DispatcherTimer collapseTimer;
        private ShelfState state = ShelfState.Hidden;
        private bool externalDrag;
        private bool draggingOut;
        private bool dragOver;
        private Point pressPoint;
        private ShelfItem pressedItem;

        public bool KeepAfterDragOut { get; set; }
        public IntPtr Handle { get; private set; }

        public ShelfWindow(ShelfStore store)
        {
            this.store = store;
            Title = "Dropline";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            AllowDrop = true;
            Width = OpenWidth;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            // Tab shown when collapsed.
            tabCount = new TextBlock { FontFamily = Mono, FontSize = 11, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center };
            var tabStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tabStack.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Accent, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center });
            tabStack.Children.Add(tabCount);
            tab = new Border
            {
                Width = TabWidth,
                Height = 72,
                Background = Paper,
                BorderBrush = Hairline,
                BorderThickness = new Thickness(1, 1, 0, 1),
                CornerRadius = new CornerRadius(10, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Child = tabStack,
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.18, Direction = 180 }
            };

            // Open card.
            var title = new TextBlock { Text = "DROPLINE", FontFamily = Mono, FontSize = 10, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
            headerCount = new TextBlock { FontFamily = Mono, FontSize = 10, Foreground = Muted, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var clear = new TextBlock { Text = "清空", FontFamily = Sans, FontSize = 11, Foreground = Muted, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "清空暂存架（文件不会被删除）" };
            clear.MouseLeftButtonUp += delegate { store.Clear(); };
            clear.MouseEnter += delegate { clear.Foreground = Accent; };
            clear.MouseLeave += delegate { clear.Foreground = Muted; };
            var header = new DockPanel { Margin = new Thickness(12, 10, 12, 8), LastChildFill = false };
            DockPanel.SetDock(clear, Dock.Right);
            header.Children.Add(clear);
            header.Children.Add(title);
            header.Children.Add(headerCount);

            dropHint = new Border
            {
                Height = 96,
                Margin = new Thickness(Margin_, 2, Margin_, Margin_),
                CornerRadius = new CornerRadius(10),
                BorderBrush = Hairline,
                BorderThickness = new Thickness(1.2),
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = "拖到这里\n暂存",
                    TextAlignment = TextAlignment.Center,
                    FontFamily = Sans,
                    FontSize = 12,
                    Foreground = Muted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            tiles = new StackPanel { Margin = new Thickness(Margin_, 0, Margin_, 4) };
            scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = tiles };

            var body = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(dropHint, Dock.Bottom);
            body.Children.Add(header);
            body.Children.Add(dropHint);
            body.Children.Add(scroller);

            card = new Border
            {
                Background = Paper,
                BorderBrush = Hairline,
                BorderThickness = new Thickness(1, 1, 0, 1),
                CornerRadius = new CornerRadius(14, 0, 0, 14),
                Child = body,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.20, Direction = 200 },
                Margin = new Thickness(14, 0, 0, 0)
            };

            var root = new Grid();
            root.Children.Add(card);
            root.Children.Add(tab);
            Content = root;

            collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            collapseTimer.Tick += delegate { collapseTimer.Stop(); CollapseIfIdle(); };

            MouseEnter += delegate { collapseTimer.Stop(); if (state == ShelfState.Tab) SetState(ShelfState.Open); };
            MouseLeave += delegate { if (!draggingOut) collapseTimer.Start(); };
            DragEnter += OnDragEnter;
            DragOver += OnDragOver;
            DragLeave += OnDragLeave;
            Drop += OnDrop;
            SourceInitialized += delegate
            {
                Handle = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(Handle);
            };

            store.Items.CollectionChanged += OnItemsChanged;
            Rebuild();
        }

        public ShelfState State { get { return state; } }

        // Called by the drag watcher: a drag started somewhere on screen.
        public void ExternalDragStarted()
        {
            externalDrag = true;
            collapseTimer.Stop();
            SetState(ShelfState.Open);
        }

        public void ExternalDragEnded()
        {
            externalDrag = false;
            collapseTimer.Stop();
            collapseTimer.Start();
        }

        public void Toggle()
        {
            if (state == ShelfState.Open) SetState(store.Items.Count > 0 ? ShelfState.Tab : ShelfState.Hidden);
            else { SetState(ShelfState.Open); collapseTimer.Start(); }
        }

        public void Restore()
        {
            SetState(store.Items.Count > 0 ? ShelfState.Tab : ShelfState.Hidden);
        }

        private void CollapseIfIdle()
        {
            if (externalDrag || draggingOut || dragOver) return;
            if (IsMouseOver) { collapseTimer.Start(); return; }
            SetState(store.Items.Count > 0 ? ShelfState.Tab : ShelfState.Hidden);
        }

        private void SetState(ShelfState next)
        {
            state = next;
            Rect area = SystemParameters.WorkArea;
            if (next == ShelfState.Hidden)
            {
                Hide();
                return;
            }

            bool open = next == ShelfState.Open;
            card.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            tab.Visibility = open ? Visibility.Collapsed : Visibility.Visible;

            double width = open ? OpenWidth : TabWidth;
            double height = open ? Math.Min(area.Height * 0.72, DesiredOpenHeight()) : 72;
            Width = width;
            Height = height;
            Left = area.Right - width;
            Top = area.Top + (area.Height - height) / 2;

            if (!IsVisible) Show();
            Topmost = false;
            Topmost = true;

            if (open)
            {
                var slide = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                card.BeginAnimation(OpacityProperty, slide);
            }
        }

        private double DesiredOpenHeight()
        {
            int count = store.Items.Count;
            double header = 38;
            double hint = 96 + Margin_ + 2;
            double list = count * (TileSize + 34);
            return Math.Max(header + hint + 8, header + list + hint + 8);
        }

        private void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Rebuild();
            if (state == ShelfState.Open) SetState(ShelfState.Open);
            else if (state == ShelfState.Tab && store.Items.Count == 0) SetState(ShelfState.Hidden);
            else if (state == ShelfState.Hidden && store.Items.Count > 0) SetState(ShelfState.Tab);
        }

        private void Rebuild()
        {
            tiles.Children.Clear();
            foreach (var item in store.Items) tiles.Children.Add(MakeTile(item));
            int n = store.Items.Count;
            tabCount.Text = n.ToString();
            headerCount.Text = n > 0 ? n.ToString() : "";
        }

        private FrameworkElement MakeTile(ShelfItem item)
        {
            var visual = new Grid { Width = TileSize, Height = TileSize, HorizontalAlignment = HorizontalAlignment.Center };
            int layers = Math.Min(item.Paths.Count, 3);
            for (int i = layers - 1; i >= 0; i--)
            {
                ImageSource source = ShelfStore.Thumbnail(item.Paths[i], (int)TileSize);
                bool photo = ShelfStore.IsImage(item.Paths[i]);
                var frame = new Border
                {
                    Width = TileSize - 12,
                    Height = TileSize - 12,
                    CornerRadius = new CornerRadius(8),
                    Background = photo ? Brushes.White : Brushes.Transparent,
                    BorderBrush = photo ? Hairline : Brushes.Transparent,
                    BorderThickness = new Thickness(photo ? 1 : 0),
                    ClipToBounds = true,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(i == 0 ? 0 : (i == 1 ? 6 : -6)),
                    Child = new Image
                    {
                        Source = source,
                        Stretch = photo ? Stretch.UniformToFill : Stretch.Uniform,
                        Width = photo ? TileSize - 14 : 56,
                        Height = photo ? TileSize - 14 : 56
                    }
                };
                if (photo) frame.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.15 };
                visual.Children.Add(frame);
            }
            if (item.IsStack)
            {
                visual.Children.Add(new Border
                {
                    Background = Ink,
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(6, 1, 6, 1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = item.Paths.Count.ToString(), Foreground = Brushes.White, FontFamily = Mono, FontSize = 10 }
                });
            }

            var label = new TextBlock
            {
                Text = item.Title,
                FontFamily = Sans,
                FontSize = 11,
                Foreground = Ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(4, 4, 4, 0)
            };

            var stack = new StackPanel();
            stack.Children.Add(visual);
            stack.Children.Add(label);

            var tile = new Border
            {
                Child = stack,
                Padding = new Thickness(0, 6, 0, 8),
                Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = string.Join("\n", item.Paths.Take(8)) + (item.Paths.Count > 8 ? "\n…" : ""),
                Tag = item
            };
            tile.MouseEnter += delegate { tile.Background = Hairline; };
            tile.MouseLeave += delegate { tile.Background = Brushes.Transparent; };
            tile.PreviewMouseLeftButtonDown += (s, e) =>
            {
                pressedItem = item;
                pressPoint = e.GetPosition(this);
                if (e.ClickCount == 2) { Open(item); e.Handled = true; }
            };
            tile.PreviewMouseMove += (s, e) =>
            {
                if (pressedItem != item || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(this);
                if (Math.Abs(p.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(p.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pressedItem = null;
                DragOut(tile, item);
            };
            tile.PreviewMouseLeftButtonUp += delegate { pressedItem = null; };
            tile.ContextMenu = MakeMenu(item);
            return tile;
        }

        private ContextMenu MakeMenu(ShelfItem item)
        {
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("打开", delegate { Open(item); }));
            menu.Items.Add(MenuItem("在资源管理器中显示", delegate { Reveal(item); }));
            menu.Items.Add(MenuItem("复制路径", delegate { Clipboard.SetText(string.Join(Environment.NewLine, item.Paths)); }));
            if (item.IsStack)
            {
                menu.Items.Add(MenuItem("拆开这一叠", delegate
                {
                    int index = store.Items.IndexOf(item);
                    store.Items.Remove(item);
                    foreach (string path in item.Paths.AsEnumerable().Reverse()) store.Items.Insert(Math.Max(0, index), new ShelfItem(new[] { path }));
                    store.Save();
                }));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("移出暂存架", delegate { store.Remove(item); }));
            menu.Opened += delegate { collapseTimer.Stop(); };
            menu.Closed += delegate { collapseTimer.Start(); };
            return menu;
        }

        private static MenuItem MenuItem(string header, RoutedEventHandler click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += click;
            return mi;
        }

        private void DragOut(UIElement source, ShelfItem item)
        {
            item.PruneMissing();
            if (item.Paths.Count == 0) { store.Remove(item); return; }

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, item.Paths.ToArray());
            if (item.Paths.Count == 1 && File.Exists(item.Paths[0]) && ShelfStore.IsImage(item.Paths[0]))
            {
                try { data.SetImage(new BitmapImage(new Uri(item.Paths[0]))); } catch { }
            }

            draggingOut = true;
            DragDropEffects result;
            try
            {
                result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                draggingOut = false;
            }

            if (result == DragDropEffects.None) { collapseTimer.Start(); return; }
            item.PruneMissing();
            if (!KeepAfterDragOut || item.Paths.Count == 0) store.Remove(item);
            else store.Save();
            collapseTimer.Start();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            dragOver = true;
            collapseTimer.Stop();
            if (state != ShelfState.Open) SetState(ShelfState.Open);
            UpdateDropEffect(e);
            if (!draggingOut) dropHint.BorderBrush = Accent;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            UpdateDropEffect(e);
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            dragOver = false;
            dropHint.BorderBrush = Hairline;
            collapseTimer.Start();
        }

        private void UpdateDropEffect(DragEventArgs e)
        {
            if (draggingOut) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) ||
                      e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text);
            // Link keeps the original file where it is: the shelf only remembers where it lives.
            e.Effects = ok ? ((e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy) : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            dragOver = false;
            dropHint.BorderBrush = Hairline;
            if (draggingOut) return;
            try
            {
                var paths = new List<string>();
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null) paths.AddRange(files);
                }
                else if (e.Data.GetDataPresent(DataFormats.Bitmap))
                {
                    var image = e.Data.GetData(DataFormats.Bitmap) as BitmapSource;
                    if (image != null) paths.Add(store.SaveImage(image));
                }
                else
                {
                    string text = e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrEmpty(text))
                    {
                        Uri uri;
                        string trimmed = text.Trim();
                        if (!trimmed.Contains("\n") && Uri.TryCreate(trimmed, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                            paths.Add(store.SaveLink(uri));
                        else
                            paths.Add(store.SaveText(text));
                    }
                }
                if (paths.Count > 0) store.Add(paths);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            e.Handled = true;
            collapseTimer.Start();
        }

        private static void Open(ShelfItem item)
        {
            if (item.IsStack) { Reveal(item); return; }
            try { Process.Start(new ProcessStartInfo(item.Paths[0]) { UseShellExecute = true }); } catch { }
        }

        private static void Reveal(ShelfItem item)
        {
            try { Process.Start("explorer.exe", "/select,\"" + item.Paths[0] + "\""); } catch { }
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
