using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dropline
{
    // One tile on the shelf. A tile can hold one file or a stack of several dropped together.
    internal sealed class ShelfItem
    {
        public ShelfItem(IEnumerable<string> paths)
        {
            Paths = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public List<string> Paths { get; private set; }
        public bool IsStack { get { return Paths.Count > 1; } }

        public string Title
        {
            get
            {
                if (Paths.Count == 0) return "";
                if (IsStack) return Paths.Count + " 个项目";
                string name = Path.GetFileName(Paths[0].TrimEnd('\\'));
                return string.IsNullOrEmpty(name) ? Paths[0] : name;
            }
        }

        public bool AllExist
        {
            get { return Paths.Count > 0 && Paths.All(p => File.Exists(p) || Directory.Exists(p)); }
        }

        public void PruneMissing()
        {
            Paths.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        }
    }

    internal sealed class ShelfStore
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico" };
        private readonly string dataFile;

        public ShelfStore()
        {
            Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dropline");
            Directory.CreateDirectory(Root);
            Clippings = Path.Combine(Root, "clippings");
            Directory.CreateDirectory(Clippings);
            dataFile = Path.Combine(Root, "shelf.txt");
            Items = new ObservableCollection<ShelfItem>();
        }

        public string Root { get; private set; }
        public string Clippings { get; private set; }
        public ObservableCollection<ShelfItem> Items { get; private set; }

        public void Load()
        {
            Items.Clear();
            if (!File.Exists(dataFile)) return;
            foreach (string line in File.ReadAllLines(dataFile, Encoding.UTF8))
            {
                if (line.Trim().Length == 0) continue;
                var item = new ShelfItem(line.Split('|'));
                item.PruneMissing();
                if (item.Paths.Count > 0) Items.Add(item);
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(dataFile, Items.Select(i => string.Join("|", i.Paths)).ToArray(), Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Add(IEnumerable<string> paths)
        {
            var item = new ShelfItem(paths);
            if (item.Paths.Count == 0) return;
            Items.Insert(0, item);
            Save();
        }

        public void Remove(ShelfItem item)
        {
            Items.Remove(item);
            Save();
        }

        public void Clear()
        {
            Items.Clear();
            Save();
        }

        // Text or an image dropped from a browser or an editor has no file behind it,
        // so it is written to a clippings folder and the tile points at that file.
        public string SaveText(string text)
        {
            string firstLine = text.Split('\n')[0].Trim();
            string name = MakeSafeName(firstLine.Length > 24 ? firstLine.Substring(0, 24) : firstLine);
            if (name.Length == 0) name = "文本";
            string path = Unique(Path.Combine(Clippings, name + ".txt"));
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }

        public string SaveLink(Uri uri)
        {
            string name = MakeSafeName(uri.Host + uri.AbsolutePath.TrimEnd('/').Replace('/', ' '));
            if (name.Length > 40) name = name.Substring(0, 40).Trim();
            string path = Unique(Path.Combine(Clippings, name + ".url"));
            File.WriteAllText(path, "[InternetShortcut]\r\nURL=" + uri.AbsoluteUri + "\r\n", Encoding.ASCII);
            return path;
        }

        public string SaveImage(BitmapSource image)
        {
            string path = Unique(Path.Combine(Clippings, "图片 " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png"));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
            return path;
        }

        public static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ImageExtensions.Contains(ext);
        }

        public static ImageSource Thumbnail(string path, int size)
        {
            try
            {
                if (File.Exists(path) && IsImage(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.DecodePixelWidth = size * 2;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { }
            return ShellIcon(path);
        }

        public static ImageSource ShellIcon(string path)
        {
            var info = new Native.SHFILEINFO();
            IntPtr result = Native.SHGetFileInfo(path, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                Native.DestroyIcon(info.hIcon);
            }
        }

        private static string MakeSafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
            return name.Trim();
        }

        private static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
        }
    }
}
