using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dropline
{
    internal enum DropAction { Compress, Convert, Rename, Zip }

    // What one action produced: the new files and a short line for the action cell.
    internal sealed class ProcessResult
    {
        public ProcessResult()
        {
            Outputs = new List<string>();
            Skipped = new List<string>();
        }

        public List<string> Outputs { get; private set; }
        public List<string> Skipped { get; private set; }
        public string Summary { get; set; }
        public string Error { get; set; }
    }

    // Drop-to-process actions. Every action writes new files into its own output folder under
    // %APPDATA%\Dropline\output, so the originals are never changed, moved or deleted.
    internal static class Processor
    {
        public const int MaxEdge = 2560;
        public const int JpegQuality = 80;
        public const int WebpQuality = 80;

        public static string ToolsDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string Cwebp { get { return Path.Combine(ToolsDir, "cwebp.exe"); } }
        public static string Dwebp { get { return Path.Combine(ToolsDir, "dwebp.exe"); } }
        public static bool CanWriteWebp { get { return File.Exists(Cwebp); } }

        public static string NewOutputDir(string root, string label)
        {
            string baseDir = Path.Combine(root, "output");
            string dir = Path.Combine(baseDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + " " + label);
            string candidate = dir;
            for (int i = 2; Directory.Exists(candidate); i++) candidate = dir + " (" + i + ")";
            Directory.CreateDirectory(candidate);
            return candidate;
        }

        // ---------- 压缩 ----------
        public static ProcessResult Compress(string root, IList<string> paths)
        {
            var result = new ProcessResult();
            string outDir = null;
            long before = 0, after = 0;
            foreach (string path in Files(paths))
            {
                if (!ShelfStore.IsImage(path)) { result.Skipped.Add(path); continue; }
                if (outDir == null) outDir = NewOutputDir(root, "压缩");
                try
                {
                    long original = new FileInfo(path).Length;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    BitmapSource image = Downscale(Load(path), MaxEdge);
                    string stem = Path.GetFileNameWithoutExtension(path);
                    string target;
                    if (ext == ".webp" && CanWriteWebp)
                    {
                        target = Unique(Path.Combine(outDir, stem + ".webp"));
                        WriteWebp(image, target, WebpQuality);
                    }
                    else if (HasAlpha(image))
                    {
                        target = Unique(Path.Combine(outDir, stem + ".png"));
                        WritePng(image, target);
                    }
                    else
                    {
                        target = Unique(Path.Combine(outDir, stem + ".jpg"));
                        WriteJpeg(image, target, JpegQuality);
                    }

                    long size = new FileInfo(target).Length;
                    if (size >= original)
                    {
                        // Re-encoding made it bigger: hand back an untouched copy instead.
                        File.Delete(target);
                        target = Unique(Path.Combine(outDir, Path.GetFileName(path)));
                        File.Copy(path, target);
                        size = original;
                    }
                    before += original;
                    after += size;
                    result.Outputs.Add(target);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    result.Skipped.Add(path);
                }
            }

            if (result.Outputs.Count == 0)
                result.Error = "没有可压缩的图片";
            else
            {
                int saved = before > 0 ? (int)Math.Round(100.0 * (before - after) / before) : 0;
                result.Summary = saved > 0 ? "省 " + saved + "% · " + Size(after) : "已是最小";
            }
            return result;
        }

        // ---------- 转格式 ----------
        public static ProcessResult Convert(string root, IList<string> paths, string format)
        {
            var result = new ProcessResult();
            format = format.ToLowerInvariant();
            if (format == "webp" && !CanWriteWebp) { result.Error = "缺少 cwebp.exe"; return result; }
            string outDir = null;
            foreach (string path in Files(paths))
            {
                if (!ShelfStore.IsImage(path)) { result.Skipped.Add(path); continue; }
                if (outDir == null) outDir = NewOutputDir(root, "转 " + format.ToUpperInvariant());
                try
                {
                    BitmapSource image = Load(path);
                    string target = Unique(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + "." + format));
                    if (format == "png") WritePng(image, target);
                    else if (format == "jpg") WriteJpeg(image, target, 90);
                    else WriteWebp(image, target, 90);
                    result.Outputs.Add(target);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    result.Skipped.Add(path);
                }
            }
            if (result.Outputs.Count == 0) result.Error = "没有可转换的图片";
            else result.Summary = result.Outputs.Count + " 张 → " + format.ToUpperInvariant();
            return result;
        }

        // ---------- 批量重命名 ----------
        // Pattern tokens: {n} running number, {name} original name, {date} today (yyyyMMdd).
        public static string RenameTarget(string pattern, string path, int index, int count, int start)
        {
            int width = Math.Max(2, (start + count - 1).ToString(CultureInfo.InvariantCulture).Length);
            string number = (start + index).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            string stem = Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(path);
            string name = pattern.Replace("{n}", number).Replace("{name}", stem).Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
            if (!pattern.Contains("{n}") && count > 1) name += "-" + number;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            if (name.Length == 0) name = number;
            return Directory.Exists(path) ? name : name + Path.GetExtension(path);
        }

        public static ProcessResult Rename(string root, IList<string> paths, string pattern, int start)
        {
            var result = new ProcessResult();
            var items = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (items.Count == 0) { result.Error = "没有文件"; return result; }
            string outDir = NewOutputDir(root, "重命名");
            for (int i = 0; i < items.Count; i++)
            {
                try
                {
                    string target = Unique(Path.Combine(outDir, RenameTarget(pattern, items[i], i, items.Count, start)));
                    if (Directory.Exists(items[i])) CopyDirectory(items[i], target);
                    else File.Copy(items[i], target);
                    result.Outputs.Add(target);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    result.Skipped.Add(items[i]);
                }
            }
            if (result.Outputs.Count == 0) result.Error = "重命名失败";
            else result.Summary = "已命名 " + result.Outputs.Count + " 个";
            return result;
        }

        // ---------- 打包 zip ----------
        public static ProcessResult Zip(string root, IList<string> paths)
        {
            var result = new ProcessResult();
            var items = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (items.Count == 0) { result.Error = "没有文件"; return result; }
            string outDir = NewOutputDir(root, "打包");
            string zipName = items.Count == 1
                ? Path.GetFileNameWithoutExtension(items[0].TrimEnd('\\')) + ".zip"
                : "Dropline " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".zip";
            string target = Unique(Path.Combine(outDir, zipName));
            try
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var stream = File.Create(target))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    foreach (string item in items)
                    {
                        if (Directory.Exists(item))
                        {
                            string baseName = Path.GetFileName(item.TrimEnd('\\'));
                            foreach (string file in Directory.GetFiles(item, "*", SearchOption.AllDirectories))
                            {
                                string rel = baseName + "/" + file.Substring(item.TrimEnd('\\').Length + 1).Replace('\\', '/');
                                AddEntry(archive, file, UniqueEntry(used, rel));
                            }
                        }
                        else AddEntry(archive, item, UniqueEntry(used, Path.GetFileName(item)));
                    }
                }
                result.Outputs.Add(target);
                result.Summary = items.Count + " 项 · " + Size(new FileInfo(target).Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                try { File.Delete(target); } catch { }
                result.Error = "打包失败";
            }
            return result;
        }

        // ---------- helpers ----------
        private static IEnumerable<string> Files(IEnumerable<string> paths)
        {
            foreach (string p in paths)
            {
                if (File.Exists(p)) yield return p;
                else if (Directory.Exists(p))
                    foreach (string f in Directory.GetFiles(p, "*", SearchOption.TopDirectoryOnly)) yield return f;
            }
        }

        public static BitmapSource Load(string path)
        {
            try
            {
                return Decode(path);
            }
            catch
            {
                // WebP needs the Windows "WebP Image Extensions" codec; fall back to dwebp when it is missing.
                if (Path.GetExtension(path).ToLowerInvariant() != ".webp" || !File.Exists(Dwebp)) throw;
                string tmp = Path.Combine(Path.GetTempPath(), "dropline-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    Run(Dwebp, Quote(path) + " -o " + Quote(tmp));
                    return Decode(tmp);
                }
                finally
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
        }

        private static BitmapSource Decode(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                // Respect the camera's EXIF orientation so photos don't come out sideways.
                BitmapSource frame = ApplyOrientation(decoder.Frames[0]);
                frame.Freeze();
                return frame;
            }
        }

        private static BitmapSource ApplyOrientation(BitmapFrame frame)
        {
            int orientation = 1;
            try
            {
                var meta = frame.Metadata as BitmapMetadata;
                if (meta != null)
                {
                    object v = meta.ContainsQuery("System.Photo.Orientation") ? meta.GetQuery("System.Photo.Orientation") : null;
                    if (v != null) orientation = System.Convert.ToInt32(v, CultureInfo.InvariantCulture);
                }
            }
            catch { }
            double angle = 0;
            bool flip = false;
            switch (orientation)
            {
                case 2: flip = true; break;
                case 3: angle = 180; break;
                case 4: angle = 180; flip = true; break;
                case 5: angle = 90; flip = true; break;
                case 6: angle = 90; break;
                case 7: angle = 270; flip = true; break;
                case 8: angle = 270; break;
                default: return frame;
            }
            var group = new TransformGroup();
            if (flip) group.Children.Add(new ScaleTransform(-1, 1));
            if (angle != 0) group.Children.Add(new RotateTransform(angle));
            return new TransformedBitmap(frame, group);
        }

        private static BitmapSource Downscale(BitmapSource image, int maxEdge)
        {
            int longEdge = Math.Max(image.PixelWidth, image.PixelHeight);
            if (longEdge <= maxEdge) return image;
            double scale = (double)maxEdge / longEdge;
            var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }

        private static bool HasAlpha(BitmapSource image)
        {
            PixelFormat f = image.Format;
            if (f != PixelFormats.Bgra32 && f != PixelFormats.Pbgra32 && f != PixelFormats.Prgba64 &&
                f != PixelFormats.Rgba64 && f != PixelFormats.Rgba128Float && f != PixelFormats.Prgba128Float &&
                !(image.Palette != null && image.Palette.Colors.Any(c => c.A < 255)))
                return false;
            // The format can carry alpha without using it (many screenshots): check the pixels.
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var row = new byte[stride];
            for (int y = 0; y < bgra.PixelHeight; y++)
            {
                bgra.CopyPixels(new Int32Rect(0, y, bgra.PixelWidth, 1), row, stride, 0);
                for (int x = 3; x < stride; x += 4) if (row[x] < 255) return true;
            }
            return false;
        }

        private static BitmapSource Flatten(BitmapSource image)
        {
            if (!HasAlpha(image)) return image;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
                dc.DrawRectangle(Brushes.White, null, rect);
                dc.DrawImage(image, rect);
            }
            var target = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }

        private static void WritePng(BitmapSource image, string target)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var s = File.Create(target)) encoder.Save(s);
        }

        private static void WriteJpeg(BitmapSource image, string target, int quality)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(Flatten(image)));
            using (var s = File.Create(target)) encoder.Save(s);
        }

        private static void WriteWebp(BitmapSource image, string target, int quality)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "dropline-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                WritePng(image, tmp);
                Run(Cwebp, "-quiet -q " + quality + " -alpha_q 100 -metadata none " + Quote(tmp) + " -o " + Quote(target));
                if (!File.Exists(target)) throw new IOException("cwebp produced no file");
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } throw new TimeoutException(exe); }
                if (p.ExitCode != 0) throw new IOException(Path.GetFileName(exe) + " exit " + p.ExitCode);
            }
        }

        private static string Quote(string s) { return "\"" + s + "\""; }

        private static void AddEntry(ZipArchive archive, string file, string name)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(file);
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = entry.Open()) input.CopyTo(output);
        }

        private static string UniqueEntry(HashSet<string> used, string name)
        {
            string candidate = name;
            string dir = name.Contains("/") ? name.Substring(0, name.LastIndexOf('/') + 1) : "";
            string file = name.Substring(dir.Length);
            string stem = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file);
            for (int i = 2; !used.Add(candidate); i++) candidate = dir + stem + " (" + i + ")" + ext;
            return candidate;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private static string Unique(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
        }

        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
