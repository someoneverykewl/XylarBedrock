using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XylarBedrock.Localization.Language;

namespace XylarBedrock.Pages.Skins
{
    public partial class SkinsPage : Page, INotifyPropertyChanged
    {
        private const int SkinsPerPage = 30;
        private readonly List<SkinEntry> allSkins = new List<SkinEntry>();
        private int pageStartIndex;
        private bool hasLoadedSkins;
        private SkinEntry selectedSkin;

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
            ShowCurrentPage();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (allSkins.Count == 0)
            {
                return;
            }

            int lastPageStart = ((allSkins.Count - 1) / SkinsPerPage) * SkinsPerPage;
            pageStartIndex = pageStartIndex <= 0 ? lastPageStart : Math.Max(0, pageStartIndex - SkinsPerPage);
            ShowCurrentPage();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (allSkins.Count == 0)
            {
                return;
            }

            pageStartIndex += SkinsPerPage;
            if (pageStartIndex >= allSkins.Count)
            {
                pageStartIndex = 0;
            }

            ShowCurrentPage();
        }

        private void SkinCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SkinEntry skin)
            {
                SelectSkin(skin);
            }
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedSkin == null)
            {
                DownloadStatusText.Text = T("SkinsPage_SelectSkinFirst", "Select a skin first.");
                return;
            }

            try
            {
                string downloadsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string skinsDirectory = Path.Combine(downloadsDirectory, "XylarBedrock Skins");
                Directory.CreateDirectory(skinsDirectory);

                string destinationPath = GetUniqueDownloadPath(Path.Combine(skinsDirectory, Path.GetFileName(selectedSkin.FilePath)));
                File.Copy(selectedSkin.FilePath, destinationPath);
                DownloadStatusText.Text = T("SkinsPage_DownloadedStatus", "Downloaded to Downloads\\XylarBedrock Skins.");
            }
            catch
            {
                DownloadStatusText.Text = T("SkinsPage_DownloadFailed", "Download failed. Try again.");
            }
        }

        private void LoadSkins()
        {
            allSkins.Clear();

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
                    if (allSkins.Any(skin => string.Equals(skin.FilePath, resolvedSkinFile, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    allSkins.Add(new SkinEntry(resolvedSkinFile));
                }
            }
        }

        private void ShowCurrentPage()
        {
            VisibleSkins.Clear();

            if (allSkins.Count == 0)
            {
                EmptySkinsText.Visibility = Visibility.Visible;
                BackButton.IsEnabled = false;
                NextButton.IsEnabled = false;
                DownloadButton.IsEnabled = false;
                SkinCounterText.Text = string.Empty;
                DownloadStatusText.Text = T("SkinsPage_NoSkinsFoundShort", "No skins found.");
                return;
            }

            EmptySkinsText.Visibility = Visibility.Collapsed;
            BackButton.IsEnabled = allSkins.Count > SkinsPerPage;
            NextButton.IsEnabled = allSkins.Count > SkinsPerPage;

            pageStartIndex = Math.Max(0, Math.Min(pageStartIndex, allSkins.Count - 1));
            int startIndex = (pageStartIndex / SkinsPerPage) * SkinsPerPage;
            pageStartIndex = startIndex;

            for (int i = 0; i < SkinsPerPage && startIndex + i < allSkins.Count; i++)
            {
                SkinEntry skin = allSkins[startIndex + i];
                skin.EnsurePreview();
                VisibleSkins.Add(skin);
            }

            int displayStart = startIndex + 1;
            int displayEnd = Math.Min(startIndex + VisibleSkins.Count, allSkins.Count);
            SkinCounterText.Text = $"{displayStart}-{displayEnd} / {allSkins.Count}";

            if (selectedSkin == null || !VisibleSkins.Contains(selectedSkin))
            {
                SelectSkin(VisibleSkins.FirstOrDefault());
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleSkins)));
        }

        private void SelectSkin(SkinEntry skin)
        {
            if (selectedSkin != null)
            {
                selectedSkin.IsSelected = false;
            }

            selectedSkin = skin;

            if (selectedSkin == null)
            {
                DownloadButton.IsEnabled = false;
                DownloadStatusText.Text = T("SkinsPage_SelectSkinHelp", "Select the skin u like and click the download button below!");
                return;
            }

            selectedSkin.IsSelected = true;
            DownloadButton.IsEnabled = true;
            DownloadStatusText.Text = T("SkinsPage_SelectSkinHelp", "Select the skin u like and click the download button below!");
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

        public class SkinEntry : INotifyPropertyChanged
        {
            private bool isSelected;
            private ImageSource previewImage;

            public SkinEntry(string filePath)
            {
                FilePath = filePath;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public string FilePath { get; }

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

            public void EnsurePreview()
            {
                if (PreviewImage != null)
                {
                    return;
                }

                PreviewImage = SkinPreviewRenderer.CreatePreview(FilePath) ?? SkinPreviewRenderer.CreateFallbackPreview(FilePath);
            }
        }

        private static class SkinPreviewRenderer
        {
            private const int PreviewWidth = 210;
            private const int PreviewHeight = 310;

            public static ImageSource CreatePreview(string skinPath)
            {
                try
                {
                    BitmapImage skin = new BitmapImage();
                    skin.BeginInit();
                    skin.CacheOption = BitmapCacheOption.OnLoad;
                    skin.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    skin.UriSource = new Uri(skinPath, UriKind.Absolute);
                    skin.EndInit();
                    skin.Freeze();

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

            public static ImageSource CreateFallbackPreview(string skinPath)
            {
                try
                {
                    BitmapImage skin = new BitmapImage();
                    skin.BeginInit();
                    skin.CacheOption = BitmapCacheOption.OnLoad;
                    skin.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    skin.DecodePixelWidth = 128;
                    skin.UriSource = new Uri(skinPath, UriKind.Absolute);
                    skin.EndInit();
                    skin.Freeze();
                    return skin;
                }
                catch
                {
                    return null;
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
