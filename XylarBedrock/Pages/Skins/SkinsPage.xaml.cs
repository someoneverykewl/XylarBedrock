using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using XylarBedrock.Localization.Language;

namespace XylarBedrock.Pages.Skins
{
    public partial class SkinsPage : Page, INotifyPropertyChanged
    {
        private readonly List<SkinEntry> allSkins = new List<SkinEntry>();
        private bool hasLoadedSkins;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<SkinEntry> VisibleSkins { get; } = new ObservableCollection<SkinEntry>();

        public SkinsPage()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (hasLoadedSkins)
            {
                return;
            }

            hasLoadedSkins = true;
            LoadSkins();
            ShowAllSkins();
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (allSkins.Count == 0)
            {
                DownloadStatusText.Text = T("SkinsPage_NoSkinsFoundShort", "No skins found.");
                return;
            }

            try
            {
                DownloadButton.IsEnabled = false;
                DownloadStatusText.Text = T("SkinsPage_PreparingCollection", "Preparing Fave's Skins...");

                string downloadsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string skinsDirectory = Path.Combine(downloadsDirectory, "XylarBedrock Skins");
                Directory.CreateDirectory(skinsDirectory);

                string packFileName = "Faves-Skins.mcpack";
                string destinationPath = GetUniqueDownloadPath(Path.Combine(skinsDirectory, packFileName));
                int exportedCount = CreateMinecraftSkinPack(allSkins, destinationPath);
                OpenDownloadedSkinPack(destinationPath);

                DownloadStatusText.Text = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    T("SkinsPage_CollectionDownloadedStatus", "Downloaded and opened Fave's Skins with {0} skins."),
                    exportedCount);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Skin download failed: {ex}");
                DownloadStatusText.Text = T("SkinsPage_DownloadFailed", "Download failed. Try again.");
            }
            finally
            {
                DownloadButton.IsEnabled = allSkins.Count > 0;
            }
        }

        private void LoadSkins()
        {
            allSkins.Clear();

            foreach (string resourcePath in GetEmbeddedSkinResourcePaths())
            {
                string fileName = GetResourceFileName(resourcePath);
                if (ShouldSkipSkinFile(fileName) || allSkins.Any(skin => string.Equals(skin.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                allSkins.Add(SkinEntry.FromResource(resourcePath));
            }

            foreach (string directory in GetSkinDirectories())
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                IEnumerable<string> skinFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedSkinImage)
                    .Where(path => Path.GetFileName(path).IndexOf("cape", StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

                foreach (string skinFile in skinFiles)
                {
                    string resolvedSkinFile = Path.GetFullPath(skinFile);
                    string fileName = Path.GetFileName(resolvedSkinFile);
                    if (allSkins.Any(skin => string.Equals(skin.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    allSkins.Add(SkinEntry.FromFile(resolvedSkinFile));
                }
            }
        }

        private void ShowAllSkins()
        {
            VisibleSkins.Clear();

            if (allSkins.Count == 0)
            {
                EmptySkinsText.Visibility = Visibility.Visible;
                DownloadButton.IsEnabled = false;
                SkinCounterText.Text = string.Empty;
                DownloadStatusText.Text = T("SkinsPage_NoSkinsFoundShort", "No skins found.");
                return;
            }

            EmptySkinsText.Visibility = Visibility.Collapsed;

            foreach (SkinEntry skin in allSkins)
            {
                skin.EnsurePreview();
                VisibleSkins.Add(skin);
            }

            DownloadButton.IsEnabled = true;
            SkinCounterText.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                T("SkinsPage_CollectionCountFormat", "{0} skins included in one pack."),
                allSkins.Count);
            DownloadStatusText.Text = T("SkinsPage_AllSkinsHelp", "Click DOWNLOADS to import every skin together.");

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleSkins)));
        }

        private static string T(string key, string fallback)
        {
            return LanguageManager.GetResource(key) as string ?? fallback;
        }

        private static string GetUniqueDownloadPath(string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }

            string directory = Path.GetDirectoryName(destinationPath);
            string fileName = Path.GetFileNameWithoutExtension(destinationPath);
            string extension = Path.GetExtension(destinationPath);

            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, $"{fileName} ({i}){extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return destinationPath;
        }

        private static int CreateMinecraftSkinPack(IEnumerable<SkinEntry> skins, string destinationPath)
        {
            const string packName = "Fave's Skins";
            const string serializeName = "xylarbedrock_faves_skins";
            List<SkinPackEntry> packEntries = new List<SkinPackEntry>();

            foreach (SkinEntry skin in skins)
            {
                try
                {
                    using (Stream skinStream = skin.OpenRead())
                    {
                        string displayName = Path.GetFileNameWithoutExtension(skin.FileName);
                        string slug = Slugify(displayName);
                        int index = packEntries.Count + 1;
                        packEntries.Add(new SkinPackEntry
                        {
                            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Skin {index}" : displayName,
                            LocalizationName = $"skin_{index:000}_{slug}",
                            TexturePath = $"skins/skin_{index:000}_{slug}.png",
                            PngBytes = BuildMinecraftReadySkinPng(skinStream)
                        });
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Skipping invalid skin '{skin.FileName}': {ex}");
                }
            }

            if (packEntries.Count == 0)
            {
                throw new InvalidDataException("No valid Minecraft skins were found for the pack.");
            }

            using (ZipArchive archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, "manifest.json", BuildSkinPackManifest(packName));
                WriteTextEntry(archive, "skins.json", BuildSkinPackJson(serializeName, packEntries));
                WriteTextEntry(archive, "texts/en_US.lang", BuildSkinPackLang(serializeName, packName, packEntries));

                foreach (SkinPackEntry entry in packEntries)
                {
                    WriteBytesEntry(archive, entry.TexturePath, entry.PngBytes);
                }

                WriteBytesEntry(archive, "pack_icon.png", packEntries[0].PngBytes);
            }

            return packEntries.Count;
        }

        private static void OpenDownloadedSkinPack(string destinationPath)
        {
            if (!destinationPath.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = destinationPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not auto-open skin pack '{destinationPath}': {ex}");
            }
        }

        private static string BuildSkinPackManifest(string displayName)
        {
            return "{\n" +
                   "  \"format_version\": 1,\n" +
                   "  \"header\": {\n" +
                   $"    \"name\": \"{JsonEscape(displayName)}\",\n" +
                   "    \"description\": \"Skins by xFaveXEditz, packed by XylarBedrock\",\n" +
                   $"    \"uuid\": \"{Guid.NewGuid()}\",\n" +
                   "    \"version\": [1, 0, 0]\n" +
                   "  },\n" +
                   "  \"modules\": [\n" +
                   "    {\n" +
                   "      \"type\": \"skin_pack\",\n" +
                   $"      \"uuid\": \"{Guid.NewGuid()}\",\n" +
                   "      \"version\": [1, 0, 0]\n" +
                   "    }\n" +
                   "  ]\n" +
                   "}\n";
        }

        private static string BuildSkinPackJson(string serializeName, IReadOnlyList<SkinPackEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($"  \"serialize_name\": \"{JsonEscape(serializeName)}\",");
            builder.AppendLine($"  \"localization_name\": \"{JsonEscape(serializeName)}\",");
            builder.AppendLine("  \"skins\": [");

            for (int i = 0; i < entries.Count; i++)
            {
                SkinPackEntry entry = entries[i];
                builder.AppendLine("    {");
                builder.AppendLine($"      \"localization_name\": \"{JsonEscape(entry.LocalizationName)}\",");
                builder.AppendLine("      \"geometry\": \"geometry.humanoid.customSlim\",");
                builder.AppendLine($"      \"texture\": \"{JsonEscape(entry.TexturePath)}\",");
                builder.AppendLine("      \"type\": \"free\"");
                builder.Append("    }");
                builder.AppendLine(i == entries.Count - 1 ? string.Empty : ",");
            }

            builder.AppendLine("  ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string BuildSkinPackLang(string serializeName, string packName, IReadOnlyList<SkinPackEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"skinpack.{serializeName}={packName}");

            foreach (SkinPackEntry entry in entries)
            {
                builder.AppendLine($"skin.{serializeName}.{entry.LocalizationName}={entry.DisplayName}");
            }

            return builder.ToString();
        }

        private static byte[] BuildMinecraftReadySkinPng(Stream sourceStream)
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                sourceStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            BitmapSource source = decoder.Frames[0];
            int sourceWidth = source.PixelWidth;
            int sourceHeight = source.PixelHeight;
            bool isSquareSkin = sourceWidth == sourceHeight && sourceWidth % 64 == 0;
            bool isClassicSkin = sourceWidth == sourceHeight * 2 && sourceWidth % 64 == 0;

            if (!isSquareSkin && !isClassicSkin)
            {
                throw new InvalidDataException($"Unsupported Minecraft skin size: {sourceWidth}x{sourceHeight}.");
            }

            int targetWidth = 64;
            int targetHeight = isClassicSkin ? 32 : 64;

            BitmapSource bgraSource = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int sourceStride = sourceWidth * 4;
            byte[] sourcePixels = new byte[sourceStride * sourceHeight];
            bgraSource.CopyPixels(sourcePixels, sourceStride, 0);

            byte[] targetPixels = ResizeNearestNeighbor(sourcePixels, sourceWidth, sourceHeight, targetWidth, targetHeight);
            BitmapSource output = BitmapSource.Create(
                targetWidth,
                targetHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                targetPixels,
                targetWidth * 4);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(output));

            using (MemoryStream outputStream = new MemoryStream())
            {
                encoder.Save(outputStream);
                return outputStream.ToArray();
            }
        }

        private static byte[] ResizeNearestNeighbor(byte[] sourcePixels, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            byte[] targetPixels = new byte[targetWidth * targetHeight * 4];

            for (int y = 0; y < targetHeight; y++)
            {
                int sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / targetHeight);
                for (int x = 0; x < targetWidth; x++)
                {
                    int sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / targetWidth);
                    int sourceIndex = (sourceY * sourceWidth + sourceX) * 4;
                    int targetIndex = (y * targetWidth + x) * 4;

                    targetPixels[targetIndex] = sourcePixels[sourceIndex];
                    targetPixels[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                    targetPixels[targetIndex + 2] = sourcePixels[sourceIndex + 2];
                    targetPixels[targetIndex + 3] = sourcePixels[sourceIndex + 3];
                }
            }

            return targetPixels;
        }

        private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
        {
            WriteBytesEntry(archive, entryName, Encoding.UTF8.GetBytes(content));
        }

        private static void WriteBytesEntry(ZipArchive archive, string entryName, byte[] content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream entryStream = entry.Open())
            {
                entryStream.Write(content, 0, content.Length);
            }
        }

        private static string Slugify(string value)
        {
            string slug = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(slug) ? "skin" : slug;
        }

        private static string JsonEscape(string value)
        {
            return value?
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"") ?? string.Empty;
        }

        private static IEnumerable<string> GetSkinDirectories()
        {
            HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string baseDirectory in GetBaseDirectories())
            {
                AddSkinDirectoryCandidates(directories, baseDirectory);
            }

            foreach (string directory in directories)
            {
                yield return directory;
            }
        }

        private static IEnumerable<string> GetBaseDirectories()
        {
            yield return AppContext.BaseDirectory;
            yield return Environment.CurrentDirectory;

            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                yield return Path.GetDirectoryName(Environment.ProcessPath);
            }

            string entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
            {
                yield return Path.GetDirectoryName(entryAssemblyPath);
            }

            string mainModulePath = null;
            try
            {
                mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch
            {
                // Some sandboxed environments do not expose MainModule. Other paths above still cover normal runs.
            }

            if (!string.IsNullOrWhiteSpace(mainModulePath))
            {
                yield return Path.GetDirectoryName(mainModulePath);
            }
        }

        private static void AddSkinDirectoryCandidates(ISet<string> directories, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return;
            }

            TryAddSkinDirectory(directories, Path.Combine(baseDirectory, "Resources", "skins"));

            try
            {
                DirectoryInfo current = new DirectoryInfo(baseDirectory);
                for (int i = 0; i < 8 && current != null; i++)
                {
                    TryAddSkinDirectory(directories, Path.Combine(current.FullName, "Resources", "skins"));
                    current = current.Parent;
                }
            }
            catch
            {
                // Invalid base paths are ignored so a single bad directory cannot break the Skins page.
            }
        }

        private static void TryAddSkinDirectory(ISet<string> directories, string path)
        {
            try
            {
                directories.Add(Path.GetFullPath(path));
            }
            catch
            {
                // Keep the loader resilient when Windows reports unusual app/extraction paths.
            }
        }

        private static bool IsSupportedSkinImage(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipSkinFile(string path)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            return !IsSupportedSkinImage(path) ||
                   Path.GetFileName(path).IndexOf("cape", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fileName.IndexOf("asteroidnqte", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<string> GetEmbeddedSkinResourcePaths()
        {
            List<string> resourcePaths = new List<string>();

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = $"{assembly.GetName().Name}.g.resources";
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return resourcePaths;
                    }

                    using (ResourceReader reader = new ResourceReader(stream))
                    {
                        foreach (DictionaryEntry entry in reader)
                        {
                            string resourcePath = entry.Key as string;
                            if (string.IsNullOrWhiteSpace(resourcePath) ||
                                !resourcePath.StartsWith("resources/skins/", StringComparison.OrdinalIgnoreCase) ||
                                ShouldSkipSkinFile(resourcePath))
                            {
                                continue;
                            }

                            resourcePaths.Add(resourcePath);
                        }
                    }
                }
            }
            catch
            {
                // If WPF resource enumeration fails, the file-system fallback below still works in development builds.
            }

            return resourcePaths.OrderBy(GetResourceFileName, StringComparer.OrdinalIgnoreCase);
        }

        private static string GetResourceFileName(string resourcePath)
        {
            string fileName = resourcePath?.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "skin.png";
            return Uri.UnescapeDataString(fileName);
        }

        private sealed class SkinPackEntry
        {
            public string DisplayName { get; set; }

            public string LocalizationName { get; set; }

            public string TexturePath { get; set; }

            public byte[] PngBytes { get; set; }
        }

        public class SkinEntry : INotifyPropertyChanged
        {
            private bool isSelected;
            private ImageSource previewImage;
            private readonly Func<Stream> openStream;

            private SkinEntry(string fileName, string filePath, string resourcePath, Func<Stream> openStream)
            {
                FileName = fileName;
                FilePath = filePath;
                ResourcePath = resourcePath;
                this.openStream = openStream;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public string FileName { get; }

            public string FilePath { get; }

            public string ResourcePath { get; }

            public ImageSource PreviewImage
            {
                get => previewImage;
                private set
                {
                    previewImage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewImage)));
                }
            }

            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected == value)
                    {
                        return;
                    }

                    isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public static SkinEntry FromFile(string filePath)
            {
                string resolvedPath = Path.GetFullPath(filePath);
                return new SkinEntry(Path.GetFileName(resolvedPath), resolvedPath, null, () => File.OpenRead(resolvedPath));
            }

            public static SkinEntry FromResource(string resourcePath)
            {
                string normalizedPath = resourcePath.Replace('\\', '/');
                return new SkinEntry(GetResourceFileName(normalizedPath), null, normalizedPath, () =>
                {
                    StreamResourceInfo resourceInfo = Application.GetResourceStream(new Uri(normalizedPath, UriKind.Relative));
                    if (resourceInfo?.Stream == null)
                    {
                        throw new FileNotFoundException("Embedded skin resource was not found.", normalizedPath);
                    }

                    return resourceInfo.Stream;
                });
            }

            public Stream OpenRead()
            {
                return openStream();
            }

            public void EnsurePreview()
            {
                if (PreviewImage != null)
                {
                    return;
                }

                PreviewImage = SkinPreviewRenderer.CreatePreview(this) ?? SkinPreviewRenderer.CreateFallbackPreview(this);
            }
        }

        private static class SkinPreviewRenderer
        {
            private const int PreviewWidth = 210;
            private const int PreviewHeight = 310;

            public static ImageSource CreatePreview(SkinEntry skinEntry)
            {
                try
                {
                    BitmapImage skin = LoadBitmap(skinEntry);

                    DrawingVisual visual = new DrawingVisual();
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
                    RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);

                    using (DrawingContext dc = visual.RenderOpen())
                    {
                        bool hasOuterLayer = skin.PixelHeight >= skin.PixelWidth;

                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(76, 0, 0, 0)), null, new Point(105, 292), 48, 10);
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), null, new Rect(46, 14, 118, 278), 18, 18);

                        DrawFlatPart(dc, skin, new SkinRegion(44, 20, 4, 12), new Rect(41, 94, 32, 96), 1.0);
                        DrawFlatPart(dc, skin, new SkinRegion(20, 20, 8, 12), new Rect(73, 94, 64, 96), 1.0);
                        DrawFlatPart(dc, skin, new SkinRegion(36, 52, 4, 12), new Rect(137, 94, 32, 96), 1.0);

                        DrawFlatPart(dc, skin, new SkinRegion(4, 20, 4, 12), new Rect(73, 190, 32, 96), 1.0);
                        DrawFlatPart(dc, skin, new SkinRegion(20, 52, 4, 12), new Rect(105, 190, 32, 96), 1.0);

                        DrawFlatPart(dc, skin, new SkinRegion(8, 8, 8, 8), new Rect(73, 30, 64, 64), 1.0);

                        if (hasOuterLayer)
                        {
                            DrawFlatPart(dc, skin, new SkinRegion(44, 36, 4, 12), Inflate(new Rect(41, 94, 32, 96), 2), 0.95);
                            DrawFlatPart(dc, skin, new SkinRegion(20, 36, 8, 12), Inflate(new Rect(73, 94, 64, 96), 2), 0.95);
                            DrawFlatPart(dc, skin, new SkinRegion(52, 52, 4, 12), Inflate(new Rect(137, 94, 32, 96), 2), 0.95);

                            DrawFlatPart(dc, skin, new SkinRegion(4, 36, 4, 12), Inflate(new Rect(73, 190, 32, 96), 2), 0.95);
                            DrawFlatPart(dc, skin, new SkinRegion(4, 52, 4, 12), Inflate(new Rect(105, 190, 32, 96), 2), 0.95);

                            DrawFlatPart(dc, skin, new SkinRegion(40, 8, 8, 8), Inflate(new Rect(73, 30, 64, 64), 3), 0.94);
                        }
                    }

                    RenderTargetBitmap preview = new RenderTargetBitmap(PreviewWidth, PreviewHeight, 96, 96, PixelFormats.Pbgra32);
                    preview.Render(visual);
                    preview.Freeze();
                    return preview;
                }
                catch
                {
                    return null;
                }
            }

            private static void DrawFlatPart(DrawingContext dc, BitmapSource skin, SkinRegion source, Rect destination, double opacity)
            {
                DrawImage(dc, Crop(skin, source.X, source.Y, source.Width, source.Height), PixelSnap(destination), opacity);
            }

            public static ImageSource CreateFallbackPreview(SkinEntry skinEntry)
            {
                try
                {
                    return LoadBitmap(skinEntry, 128);
                }
                catch
                {
                    return null;
                }
            }

            private static BitmapImage LoadBitmap(SkinEntry skinEntry, int decodePixelWidth = 0)
            {
                using (Stream stream = skinEntry.OpenRead())
                {
                    BitmapImage skin = new BitmapImage();
                    skin.BeginInit();
                    skin.CacheOption = BitmapCacheOption.OnLoad;
                    skin.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (decodePixelWidth > 0)
                    {
                        skin.DecodePixelWidth = decodePixelWidth;
                    }

                    skin.StreamSource = stream;
                    skin.EndInit();
                    skin.Freeze();
                    return skin;
                }
            }

            private static void DrawBox(DrawingContext dc, BitmapSource skin, SkinRegion front, SkinRegion side, SkinRegion top, Rect frontRect, double depth, bool sideOnLeft, double opacity)
            {
                BitmapSource frontTexture = Crop(skin, front.X, front.Y, front.Width, front.Height);
                BitmapSource sideTexture = Crop(skin, side.X, side.Y, side.Width, side.Height);
                BitmapSource topTexture = Crop(skin, top.X, top.Y, top.Width, top.Height);

                Point[] topPoints;
                Point[] sidePoints;

                if (sideOnLeft)
                {
                    topPoints = new[]
                    {
                        new Point(frontRect.Left, frontRect.Top),
                        new Point(frontRect.Left - depth, frontRect.Top - depth),
                        new Point(frontRect.Right - depth, frontRect.Top - depth),
                        new Point(frontRect.Right, frontRect.Top)
                    };
                    sidePoints = new[]
                    {
                        new Point(frontRect.Left, frontRect.Top),
                        new Point(frontRect.Left - depth, frontRect.Top - depth),
                        new Point(frontRect.Left - depth, frontRect.Bottom - depth),
                        new Point(frontRect.Left, frontRect.Bottom)
                    };
                }
                else
                {
                    topPoints = new[]
                    {
                        new Point(frontRect.Left, frontRect.Top),
                        new Point(frontRect.Left + depth, frontRect.Top - depth),
                        new Point(frontRect.Right + depth, frontRect.Top - depth),
                        new Point(frontRect.Right, frontRect.Top)
                    };
                    sidePoints = new[]
                    {
                        new Point(frontRect.Right, frontRect.Top),
                        new Point(frontRect.Right + depth, frontRect.Top - depth),
                        new Point(frontRect.Right + depth, frontRect.Bottom - depth),
                        new Point(frontRect.Right, frontRect.Bottom)
                    };
                }

                DrawImageInPolygon(dc, topTexture, topPoints, opacity * 0.88);
                DrawImageInPolygon(dc, sideTexture, sidePoints, opacity * 0.68);
                DrawImage(dc, frontTexture, frontRect, opacity);

                Pen outlinePen = new Pen(new SolidColorBrush(Color.FromArgb(95, 0, 0, 0)), 1);
                outlinePen.Freeze();
                dc.DrawRectangle(null, outlinePen, frontRect);
                DrawPolygonOutline(dc, topPoints, outlinePen);
                DrawPolygonOutline(dc, sidePoints, outlinePen);

                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), null, new Rect(frontRect.Left, frontRect.Top, frontRect.Width, Math.Max(2, frontRect.Height * 0.08)));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), null, new Rect(frontRect.Left, frontRect.Bottom - Math.Max(2, frontRect.Height * 0.08), frontRect.Width, Math.Max(2, frontRect.Height * 0.08)));
            }

            private static void DrawImage(DrawingContext dc, ImageSource image, Rect destination, double opacity)
            {
                if (image == null)
                {
                    return;
                }

                dc.PushOpacity(opacity);
                dc.DrawImage(image, destination);
                dc.Pop();
            }

            private static void DrawImageInPolygon(DrawingContext dc, ImageSource image, Point[] points, double opacity)
            {
                if (image == null || points == null || points.Length < 4)
                {
                    return;
                }

                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(points[0], true, true);
                    context.PolyLineTo(new[] { points[1], points[2], points[3] }, true, true);
                }

                geometry.Freeze();

                ImageBrush brush = new ImageBrush(image)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                brush.Freeze();

                dc.PushOpacity(opacity);
                dc.DrawGeometry(brush, null, geometry);
                dc.Pop();
            }

            private static void DrawPolygonOutline(DrawingContext dc, Point[] points, Pen pen)
            {
                if (points == null || points.Length < 4)
                {
                    return;
                }

                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(points[0], false, true);
                    context.PolyLineTo(new[] { points[1], points[2], points[3] }, true, true);
                }

                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }

            private static void DrawRimLight(DrawingContext dc)
            {
                Pen lightPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1);
                lightPen.Freeze();
                dc.DrawLine(lightPen, new Point(55, 68), new Point(91, 20));
                dc.DrawLine(lightPen, new Point(132, 28), new Point(158, 98));
            }

            private static Rect Inflate(Rect rect, double amount)
            {
                rect.Inflate(amount, amount);
                return rect;
            }

            private static Rect PixelSnap(Rect rect)
            {
                return new Rect(
                    Math.Round(rect.X),
                    Math.Round(rect.Y),
                    Math.Round(rect.Width),
                    Math.Round(rect.Height));
            }

            private static BitmapSource Crop(BitmapSource source, int x, int y, int width, int height)
            {
                double scaleX = source.PixelWidth / 64.0;
                double scaleY = source.PixelHeight >= source.PixelWidth ? source.PixelHeight / 64.0 : source.PixelHeight / 32.0;

                int cropX = Clamp((int)Math.Round(x * scaleX), 0, source.PixelWidth - 1);
                int cropY = Clamp((int)Math.Round(y * scaleY), 0, source.PixelHeight - 1);
                int cropWidth = Math.Max(1, Math.Min((int)Math.Round(width * scaleX), source.PixelWidth - cropX));
                int cropHeight = Math.Max(1, Math.Min((int)Math.Round(height * scaleY), source.PixelHeight - cropY));

                CroppedBitmap cropped = new CroppedBitmap(source, new Int32Rect(cropX, cropY, cropWidth, cropHeight));
                cropped.Freeze();
                return cropped;
            }

            private static int Clamp(int value, int min, int max)
            {
                if (value < min)
                {
                    return min;
                }

                return value > max ? max : value;
            }

            private struct SkinRegion
            {
                public SkinRegion(int x, int y, int width, int height)
                {
                    X = x;
                    Y = y;
                    Width = width;
                    Height = height;
                }

                public int X { get; }
                public int Y { get; }
                public int Width { get; }
                public int Height { get; }
            }
        }
    }
}
