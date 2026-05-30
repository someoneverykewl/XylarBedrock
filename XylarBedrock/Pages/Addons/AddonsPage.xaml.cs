using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Windows.Storage;
using Windows.System;
using XylarBedrock.Classes;
using XylarBedrock.Handlers;
using XylarBedrock.Localization.Language;

namespace XylarBedrock.Pages.Addons
{
    public partial class AddonsPage : Page, INotifyPropertyChanged
    {
        private const int FeaturedMosaicCount = 4;
        private const int PromoAddonsCount = 2;
        private const int ShelfOneAnimatedRemoteCount = 10;
        private const int ShelfTwoVisibleCount = 8;
        private const double ShelfScrollPadding = 18;
        private const int MaxEmptyCatalogRecoveryAttempts = 2;
        private static readonly TimeSpan ShelfAnimationDuration = TimeSpan.FromMilliseconds(260);
        private static readonly TimeSpan AddonsEntryOverlayDuration = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan AddonsSearchOverlayDuration = TimeSpan.FromSeconds(4);
        private const int MaxDomWaitAttempts = 8;
        private static readonly string[] AllowedBrowserSchemes = { "http", "https", "about", "data", "file" };
        private const string CurseForgeExtractionScript = """
(() => {
  const clean = (value) => (value || '').replace(/\s+/g, ' ').trim();
  const bodyText = document.body ? clean(document.body.innerText) : '';
  const pageText = document.body ? document.body.innerText : '';
  const isChallengePage = /just a moment|checking your browser|verify you are human/i.test(bodyText);

  const projectSelector = "a[href*='/minecraft-bedrock/addons/']";
  const projectCardSelector = "a[href*='/minecraft-bedrock/addons/']:not([href*='/download/'])";
  const titleAnchors = Array.from(document.querySelectorAll(projectSelector))
    .filter(anchor => {
      const href = anchor.getAttribute('href') || '';
      const text = clean(anchor.textContent);
      return text.length > 0 && !href.includes('/download/');
    });

  const seen = new Set();
  const items = [];

  const isMetaLine = (line) => {
    return /^(\d+(\.\d+)?[KM]?|[A-Z][a-z]{2} \d{1,2}, \d{4}|\d+(\.\d+)?\s?(KB|MB|GB)|(\+ \d+ Versions)|(26\.\d+)|(1\.\d+(\.\d+)*)|Addons|Download)$/i.test(line);
  };

  for (const titleAnchor of titleAnchors) {
    const pageUri = titleAnchor.href;
    if (!pageUri || seen.has(pageUri)) continue;

    let container = titleAnchor;
    while (container && container !== document.body) {
      const addonLinks = container.querySelectorAll(projectCardSelector).length;
      const hasDownload = !!container.querySelector("a[href*='/download/']");
      if (hasDownload && addonLinks <= 2) break;
      container = container.parentElement;
    }

    if (!container || container === document.body) continue;

    const downloadAnchor = container.querySelector("a[href*='/download/']");
    if (!downloadAnchor) continue;

    const title = clean(titleAnchor.textContent);
    if (!title || seen.has(pageUri)) continue;
    seen.add(pageUri);

    const anchors = Array.from(container.querySelectorAll("a"));
    let author = "";
    for (const anchor of anchors) {
      if (anchor === titleAnchor || anchor === downloadAnchor) continue;

      const text = clean(anchor.textContent).replace(/^By\s*/i, "");
      const href = anchor.getAttribute('href') || '';
      if (!text) continue;
      if (href.includes('/minecraft-bedrock/search') || href.includes('/minecraft-bedrock/addons/') || href.includes('/download/')) continue;
      if (/view|download/i.test(text)) continue;
      author = text;
      break;
    }

    const images = Array.from(container.querySelectorAll("img[src]"));
    const image = images.find(img => {
      const src = img.src || '';
      const alt = clean(img.alt);
      return /forgecdn|curseforge/i.test(src) && !/placeholder/i.test(alt);
    }) || images[0];

    const lines = Array.from(new Set(container.innerText.split(/\n+/).map(clean).filter(Boolean)));
    const description = lines.find(line =>
      line.length > 35 &&
      line !== title &&
      line !== author &&
      !/^By\s/i.test(line) &&
      !isMetaLine(line)) || "";

    const downloadsText = lines.find(line => /^\d+(\.\d+)?[KM]$/.test(line)) || "";
    const updatedText = lines.find(line => /^[A-Z][a-z]{2} \d{1,2}, \d{4}$/.test(line)) || "";
    const fileSizeText = lines.find(line => /^\d+(\.\d+)?\s?(KB|MB|GB)$/.test(line)) || "";
    const gameVersionText = lines.find(line => /^(26\.\d+|1\.\d+(\.\d+)*)$/.test(line)) || "";

    items.push({
      title,
      author,
      description,
      imagePath: image ? image.src : "",
      installUri: downloadAnchor.href,
      pageUri,
      downloadsText,
      updatedText,
      fileSizeText,
      gameVersionText,
      sourceLabel: 'CurseForge Bedrock Add-ons'
    });
  }

  const countMatch = pageText.match(/([\d,]+)\s+Projects found/i);
  const pageLinks = Array.from(document.querySelectorAll("a[href*='class=addons'][href*='page=']"))
    .map(anchor => {
      try {
        return parseInt(new URL(anchor.href, location.href).searchParams.get('page') || '', 10);
      } catch {
        return NaN;
      }
    })
    .filter(value => !Number.isNaN(value));

  let currentPage = 1;
  try {
    currentPage = parseInt(new URL(location.href).searchParams.get('page') || '1', 10);
  } catch {
    currentPage = 1;
  }

  return JSON.stringify({
    isChallengePage,
    totalCountText: countMatch ? countMatch[1] : "",
    currentPage,
    maxKnownPage: pageLinks.length > 0 ? Math.max(...pageLinks) : currentPage,
    items
  });
})()
""";

        private const string CurseForgeDownloadAssistScript = """
(() => {
  const clean = (value) => (value || '').replace(/\s+/g, ' ').trim();
  const current = location.href;
  const anchors = Array.from(document.querySelectorAll('a[href]'));

  const directPackage = anchors.find(anchor => {
    const href = anchor.href || '';
    return /\.(mcpack|mcaddon)(\?|#|$)/i.test(href) ||
      /edge\.forgecdn\.net|mediafilez\.forgecdn\.net|download\.curseforge\.com/i.test(href);
  });

  if (directPackage) {
    directPackage.click();
    return JSON.stringify({ clicked: true, url: directPackage.href, reason: 'direct-package' });
  }

  const downloadLink = anchors.find(anchor => {
    const href = anchor.href || '';
    const text = clean(anchor.textContent);
    if (!href || href === current) return false;
    return /\/download\/\d+|\/download\/file|\/files\/\d+\/download/i.test(href) ||
      /download|continue|click here/i.test(text);
  });

  if (downloadLink) {
    downloadLink.click();
    return JSON.stringify({ clicked: true, url: downloadLink.href, reason: 'download-link' });
  }

  const buttons = Array.from(document.querySelectorAll('button,[role="button"]'));
  const button = buttons.find(element => /download|continue|click here/i.test(clean(element.textContent)));
  if (button) {
    button.click();
    return JSON.stringify({ clicked: true, url: '', reason: 'button' });
  }

  return JSON.stringify({
    clicked: false,
    url: '',
    reason: /just a moment|checking your browser|verify you are human/i.test(document.body ? document.body.innerText : '')
      ? 'challenge'
      : 'waiting'
  });
})()
""";

        private readonly HashSet<string> loadedAddonUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim fetchLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim downloadLock = new SemaphoreSlim(1, 1);
        private FileSystemWatcher downloadsWatcher;
        private CancellationTokenSource downloadsWatcherToken;
        private TaskCompletionSource<string> pendingDownloadTaskSource;
        private int importTriggered;
        private bool downloadBrowserInitialized;
        private bool downloadBrowserFailed;
        private bool hiddenBrowserInitialized;
        private bool hiddenBrowserFailed;
        private int lastLoadedPage;
        private int maxKnownPage = 1;
        private string pendingDownloadTitle = string.Empty;
        private string currentSearchText = string.Empty;
        private string totalCountText = string.Empty;
        private readonly AddonEntry bundledActionsAddon;
        private AddonEntry featuredAddon;
        private AddonEntry selectedAddon;
        private string currentHeaderTitle = "Addons";
        private DispatcherTimer shelfOneScrollTimer;
        private double shelfOneAnimationFrom;
        private double shelfOneAnimationTo;
        private DateTime shelfOneAnimationStartedAt;
        private bool isAddonDownloadBusy;
        private bool isCatalogRefreshRunning;
        private bool isAddonsOverlayBusy;
        private bool isEmptyCatalogRecoveryRunning;
        private bool isDownloadAssistRunning;
        private int emptyCatalogRecoveryAttempts;
        private int downloadAssistSessionId;
        private int overlaySessionId;

        public event PropertyChangedEventHandler PropertyChanged;

        public AddonEntry FeaturedAddon
        {
            get => featuredAddon;
            private set
            {
                if (ReferenceEquals(featuredAddon, value)) return;

                featuredAddon = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<AddonEntry> VisibleAddons { get; } = new ObservableCollection<AddonEntry>();
        public ObservableCollection<AddonEntry> SpotlightAddons { get; } = new ObservableCollection<AddonEntry>();
        public ObservableCollection<AddonEntry> PromoAddons { get; } = new ObservableCollection<AddonEntry>();
        public ObservableCollection<AddonEntry> ShelfOneAddons { get; } = new ObservableCollection<AddonEntry>();
        public ObservableCollection<AddonEntry> ShelfTwoAddons { get; } = new ObservableCollection<AddonEntry>();
        public ObservableCollection<AddonEntry> RelatedAddons { get; } = new ObservableCollection<AddonEntry>();
        public ObservableCollection<AddonEntry> CreatorAddons { get; } = new ObservableCollection<AddonEntry>();

        public AddonEntry SelectedAddon
        {
            get => selectedAddon;
            private set
            {
                if (ReferenceEquals(selectedAddon, value)) return;

                selectedAddon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedAddonDescription));
                OnPropertyChanged(nameof(SelectedAddonAuthorText));
                OnPropertyChanged(nameof(SelectedAddonMetaText));
                OnPropertyChanged(nameof(SelectedAddonActionText));
            }
        }

        public string CurrentHeaderTitle
        {
            get => currentHeaderTitle;
            private set
            {
                if (currentHeaderTitle == value) return;

                currentHeaderTitle = value;
                OnPropertyChanged();
            }
        }

        public string SelectedAddonDescription =>
            string.IsNullOrWhiteSpace(SelectedAddon?.Description)
                ? T("AddonsPage_DefaultDescription", "Open this add-on to install it from inside the launcher.")
                : SelectedAddon.Description;

        public string SelectedAddonAuthorText =>
            string.IsNullOrWhiteSpace(SelectedAddon?.Author)
                ? T("AddonsPage_UnknownAuthor", "Unknown author")
                : string.Format(CultureInfo.CurrentCulture, T("AddonsPage_ByAuthorFormat", "By {0}"), SelectedAddon.Author);

        public string SelectedAddonMetaText =>
            string.Format(
                CultureInfo.CurrentCulture,
                T("AddonsPage_DetailMetaFormat", "1 pack - {0} - {1}"),
                string.IsNullOrWhiteSpace(SelectedAddon?.GameVersionText)
                    ? T("AddonsPage_BedrockTag", "Bedrock")
                    : SelectedAddon.GameVersionText,
                string.IsNullOrWhiteSpace(SelectedAddon?.SourceLabel)
                    ? T("AddonsPage_TrustedSourceFallback", "Trusted source")
                    : SelectedAddon.SourceLabel);

        public string SelectedAddonActionText =>
            SelectedAddon?.IsCustom == true
                ? T("AddonsPage_InstallButton", "Install")
                : T("AddonsPage_DownloadButton", "Download");

        public AddonsPage()
        {
            bundledActionsAddon = AddonsCatalogHandler.BuildDefaultCatalog().FirstOrDefault() ?? new AddonEntry()
            {
                Title = "Actions & Stuff",
                Author = "XylarBedrock"
            };
            FeaturedAddon = bundledActionsAddon;
            DataContext = this;
            InitializeComponent();
            StartOverlaySpinner();
            Loaded += AddonsPage_Loaded;
            Unloaded += AddonsPage_Unloaded;
        }

        public void WarmUpCatalog()
        {
            if (VisibleAddons.Count > 0) return;

            try
            {
                LoadCachedCatalog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private async void AddonsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await ShowAddonsOverlayAsync(async () =>
                {
                    ShowCatalogView();
                    _ = EnsureHiddenBrowserReadyAsync();
                    _ = EnsureHiddenDownloadBrowserReadyAsync();

                    bool loadedFromCache = VisibleAddons.Count > 0 || LoadCachedCatalog();
                    if (!loadedFromCache)
                    {
                        await ReloadRemoteAddonsAsync(showLoading: false);
                    }
                    else if (!isCatalogRefreshRunning && !AddonsCatalogHandler.HasFreshRemoteCache())
                    {
                        _ = ReloadRemoteAddonsAsync(showLoading: false, preserveCurrentResults: true);
                    }
                }, AddonsEntryOverlayDuration);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                NoResultsState.Visibility = Visibility.Visible;
                NoResultsText.Text = T("AddonsPage_LoadFailed", "Couldn't load addons right now.");
                SetLoadingState(false, string.Empty);
                QueueEmptyCatalogRecovery("Addons page load failed.");
            }
        }

        private void AddonsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            overlaySessionId++;
            SetAddonsOverlayVisible(false);
            SetLoadingState(false, string.Empty);
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (isAddonsOverlayBusy) return;

            ShowCatalogView();
            await ShowAddonsOverlayAsync(() => ReloadRemoteAddonsAsync(showLoading: false), AddonsSearchOverlayDuration);
        }

        private async void ShowAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (isAddonsOverlayBusy) return;

            ShowCatalogView();
            SearchBox.Text = string.Empty;
            await ShowAddonsOverlayAsync(() => ReloadRemoteAddonsAsync(showLoading: false), AddonsSearchOverlayDuration);
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || isAddonsOverlayBusy) return;

            e.Handled = true;
            ShowCatalogView();
            await ShowAddonsOverlayAsync(() => ReloadRemoteAddonsAsync(showLoading: false), AddonsSearchOverlayDuration);
        }

        private async void AddonCard_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not AddonEntry addon || isAddonsOverlayBusy) return;

            await ShowAddonsOverlayAsync(() =>
            {
                OpenAddonDetails(addon);
                return Task.CompletedTask;
            }, AddonsSearchOverlayDuration);
        }

        private async void DownloadSelectedAddonButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAddon == null || isAddonDownloadBusy) return;

            await InstallAddonAsync(SelectedAddon);
        }

        private async Task InstallAddonAsync(AddonEntry addon)
        {
            if (addon == null) return;

            SetAddonDownloadState(true, T("AddonsPage_PreparingDownload", "Getting your addon ready..."));

            try
            {
                if (addon.IsLocal || addon.IsCustom)
                {
                    try
                    {
                        string addonPath = addon.IsCustom
                            ? AddonsCatalogHandler.EnsureCustomAddonPackagePath()
                            : addon.LocalPackagePath;

                        await OpenAddonFileAsync(addonPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            string.Format(CultureInfo.CurrentCulture, T("AddonsPage_ActionsFailed", "Couldn't open the bundled addon.\n{0}"), ex.Message),
                            App.DisplayName,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }

                    return;
                }

                if (string.IsNullOrWhiteSpace(addon.InstallUri)) return;

                if (isCatalogRefreshRunning && VisibleAddons.Count == 0)
                {
                    MessageBox.Show(
                        T("AddonsPage_WaitForLoad", "Wait a second, addons are still loading."),
                        App.DisplayName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                try
                {
                    SetAddonDownloadState(true, string.Format(CultureInfo.CurrentCulture, T("AddonsPage_DownloadingFormat", "Downloading {0}..."), addon.Title));
                    string downloadedAddonPath = await DownloadAddonPackageAsync(addon);
                    AddonsCatalogHandler.RememberDownloadedAddonPackage(addon, downloadedAddonPath);
                    SetAddonDownloadState(true, T("AddonsPage_OpeningAddon", "Opening addon in Minecraft..."));
                    await OpenAddonFileAsync(downloadedAddonPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        string.Format(CultureInfo.CurrentCulture, T("AddonsPage_DownloadFailed", "Couldn't download this addon right now.\n{0}"), ex.Message),
                        App.DisplayName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                SetAddonDownloadState(false, string.Empty);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (DetailViewRoot.Visibility == Visibility.Visible)
            {
                ShowCatalogView();
            }
        }

        private void PageContent_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (AddonsLoadingOverlay.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                return;
            }

            ScrollViewer activeScrollViewer = GetActiveScrollViewer();
            if (activeScrollViewer == null) return;

            double delta = e.Delta / 1.15;
            double targetOffset = Math.Max(
                0,
                Math.Min(activeScrollViewer.ScrollableHeight, activeScrollViewer.VerticalOffset - delta));

            if (Math.Abs(targetOffset - activeScrollViewer.VerticalOffset) < 0.1) return;

            activeScrollViewer.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
        }

        private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (lastLoadedPage >= maxKnownPage) return;

            await LoadRemoteAddonsPageAsync(lastLoadedPage + 1, reset: false);
        }

        private void ShelfOneScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            ScrollShelfOneBy(-GetShelfScrollStep());
        }

        private void ShelfOneScrollRight_Click(object sender, RoutedEventArgs e)
        {
            ScrollShelfOneBy(GetShelfScrollStep());
        }

        private void ShelfOneScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateShelfOneScrollButtons();
        }

        private async Task ReloadRemoteAddonsAsync(bool showLoading = true, bool preserveCurrentResults = false)
        {
            if (isCatalogRefreshRunning) return;

            isCatalogRefreshRunning = true;
            currentSearchText = SearchBox.Text?.Trim() ?? string.Empty;

            try
            {
                if (!preserveCurrentResults)
                {
                    lastLoadedPage = 0;
                    maxKnownPage = 1;
                    totalCountText = string.Empty;
                    loadedAddonUris.Clear();
                    VisibleAddons.Clear();
                    RebuildMarketplaceShelves();
                    UpdateCatalogLabels();
                }

                await LoadRemoteAddonsPageAsync(1, reset: true, showLoading: showLoading);
            }
            finally
            {
                isCatalogRefreshRunning = false;
            }
        }

        private async Task LoadRemoteAddonsPageAsync(int page, bool reset, bool showLoading = true, bool allowEmptyRecovery = true)
        {
            await fetchLock.WaitAsync();
            bool shouldRecoverEmptyCatalog = false;

            try
            {
                if (showLoading)
                {
                    SetLoadingState(true, page == 1
                        ? T("AddonsPage_LoadingText", "Loading addons...")
                        : string.Format(CultureInfo.CurrentCulture, T("AddonsPage_LoadingPageFormat", "Loading page {0}..."), page));
                }
                else
                {
                    LoadMoreButton.IsEnabled = false;
                }

                if (reset)
                {
                    VisibleAddons.Clear();
                    loadedAddonUris.Clear();
                    RebuildMarketplaceShelves();
                }

                string trustedTotalCountText = string.Empty;
                int trustedCurrentPage = page;
                int trustedMaxKnownPage = page;
                AddonCatalogPage trustedPayload = await AddonsCatalogHandler.FetchTrustedCatalogAsync(currentSearchText, page);
                if (trustedPayload.ProviderSucceeded)
                {
                    foreach (AddonEntry addon in trustedPayload.Items)
                    {
                        AddVisibleAddon(addon);
                    }

                    trustedCurrentPage = Math.Max(page, trustedPayload.CurrentPage);
                    trustedMaxKnownPage = Math.Max(trustedPayload.MaxKnownPage, trustedCurrentPage);
                    trustedTotalCountText = trustedPayload.TotalCountText ?? string.Empty;
                }

                if (!await EnsureHiddenBrowserReadyAsync())
                {
                    if (VisibleAddons.Count == 0 && !HasHomeFallbackAddon())
                    {
                        NoResultsState.Visibility = Visibility.Visible;
                        NoResultsText.Text = T("AddonsPage_NoResultsText", "Try another search or reload the list.");
                        shouldRecoverEmptyCatalog = true;
                    }
                    else if (VisibleAddons.Count == 0)
                    {
                        shouldRecoverEmptyCatalog = true;
                    }
                    else
                    {
                        lastLoadedPage = Math.Max(lastLoadedPage, trustedCurrentPage);
                        maxKnownPage = Math.Max(trustedMaxKnownPage, lastLoadedPage);
                        totalCountText = trustedTotalCountText;
                        FinalizeCatalogPage();
                        if (VisibleAddons.Count > 0)
                        {
                            AddonsCatalogHandler.SaveCachedRemoteAddons(VisibleAddons);
                        }
                    }

                    return;
                }

                int payloadCurrentPage = 1;
                int payloadMaxKnownPage = 1;
                string payloadTotalCountText = string.Empty;

                foreach (string requestUrl in AddonsCatalogHandler.BuildCurseForgeSearchUrls(currentSearchText, page))
                {
                    CurseForgePagePayload payload = await FetchAddonsPageAsync(requestUrl);
                    payloadCurrentPage = Math.Max(payloadCurrentPage, payload.CurrentPage);
                    payloadMaxKnownPage = Math.Max(payloadMaxKnownPage, payload.MaxKnownPage);

                    if (string.IsNullOrWhiteSpace(payloadTotalCountText))
                    {
                        payloadTotalCountText = payload.TotalCountText ?? string.Empty;
                    }

                    foreach (CurseForgePageItem item in payload.Items ?? new List<CurseForgePageItem>())
                    {
                        AddVisibleAddon(new AddonEntry()
                        {
                            Title = item.Title ?? string.Empty,
                            Author = string.IsNullOrWhiteSpace(item.Author) ? T("AddonsPage_UnknownAuthor", "Unknown author") : item.Author,
                            Description = item.Description ?? string.Empty,
                            SourceLabel = string.IsNullOrWhiteSpace(item.SourceLabel) ? "CurseForge" : item.SourceLabel,
                            ImagePath = item.ImagePath ?? string.Empty,
                            InstallUri = item.InstallUri ?? string.Empty,
                            PageUri = item.PageUri ?? string.Empty,
                            DownloadsText = item.DownloadsText ?? string.Empty,
                            UpdatedText = string.IsNullOrWhiteSpace(item.UpdatedText) ? T("AddonsPage_UnknownDate", "Unknown date") : item.UpdatedText,
                            FileSizeText = item.FileSizeText ?? string.Empty,
                            GameVersionText = string.IsNullOrWhiteSpace(item.GameVersionText) ? T("AddonsPage_BedrockTag", "Bedrock") : item.GameVersionText
                        });
                    }
                }

                lastLoadedPage = Math.Max(lastLoadedPage, Math.Max(trustedCurrentPage, payloadCurrentPage));
                maxKnownPage = Math.Max(Math.Max(trustedMaxKnownPage, payloadMaxKnownPage), lastLoadedPage);
                totalCountText = string.IsNullOrWhiteSpace(payloadTotalCountText)
                    ? trustedTotalCountText
                    : payloadTotalCountText;

                FinalizeCatalogPage();
                AddonsCatalogHandler.SaveCachedRemoteAddons(VisibleAddons);
                shouldRecoverEmptyCatalog = VisibleAddons.Count == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);

                if (VisibleAddons.Count == 0 && !HasHomeFallbackAddon())
                {
                    NoResultsState.Visibility = Visibility.Visible;
                    NoResultsText.Text = T("AddonsPage_LoadFailed", "Couldn't load addons right now.");
                    shouldRecoverEmptyCatalog = true;
                }
                else if (VisibleAddons.Count == 0)
                {
                    shouldRecoverEmptyCatalog = true;
                }
                else
                {
                    ResultsSummaryText.Text = T("AddonsPage_UsingCacheFallback", "Showing the last saved add-ons while refresh finishes later.");
                    NoResultsState.Visibility = Visibility.Collapsed;
                }
            }
            finally
            {
                SetLoadingState(false, string.Empty);
                fetchLock.Release();

                if (allowEmptyRecovery && shouldRecoverEmptyCatalog)
                {
                    QueueEmptyCatalogRecovery("No add-ons were detected after catalog load.");
                }
            }
        }

        private async Task<bool> EnsureHiddenBrowserReadyAsync()
        {
            if (hiddenBrowserInitialized) return true;
            if (hiddenBrowserFailed) return false;

            try
            {
                await ScraperBrowser.EnsureCoreWebView2Async();
                ScraperBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                ScraperBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                ScraperBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                ScraperBrowser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                ScraperBrowser.CoreWebView2.Settings.IsZoomControlEnabled = false;
                ScraperBrowser.CoreWebView2.NewWindowRequested += ScraperBrowser_NewWindowRequested;
                ScraperBrowser.CoreWebView2.NavigationStarting += HiddenBrowser_NavigationStarting;
                ScraperBrowser.CoreWebView2.LaunchingExternalUriScheme += HiddenBrowser_LaunchingExternalUriScheme;
                hiddenBrowserInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                hiddenBrowserFailed = true;
                Debug.WriteLine(ex);
                return false;
            }
        }

        private async Task<bool> EnsureHiddenDownloadBrowserReadyAsync()
        {
            if (downloadBrowserInitialized) return true;
            if (downloadBrowserFailed) return false;

            try
            {
                await DownloadBrowser.EnsureCoreWebView2Async();
                DownloadBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                DownloadBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                DownloadBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                DownloadBrowser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                DownloadBrowser.CoreWebView2.Settings.IsZoomControlEnabled = false;
                DownloadBrowser.CoreWebView2.NewWindowRequested += DownloadBrowser_NewWindowRequested;
                DownloadBrowser.CoreWebView2.DownloadStarting += DownloadBrowser_DownloadStarting;
                DownloadBrowser.CoreWebView2.NavigationStarting += HiddenBrowser_NavigationStarting;
                DownloadBrowser.NavigationCompleted += DownloadBrowser_NavigationCompleted;
                DownloadBrowser.CoreWebView2.LaunchingExternalUriScheme += HiddenBrowser_LaunchingExternalUriScheme;
                downloadBrowserInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                downloadBrowserFailed = true;
                Debug.WriteLine(ex);
                return false;
            }
        }

        private void DownloadBrowser_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (pendingDownloadTaskSource == null || !e.IsSuccess) return;

            int assistSession = downloadAssistSessionId;
            _ = AssistCurseForgeDownloadAsync(assistSession);
        }

        private async Task<CurseForgePagePayload> FetchAddonsPageAsync(string requestUrl)
        {
            var navigationCompletion = new TaskCompletionSource<bool>();

            void Handler(object sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                ScraperBrowser.NavigationCompleted -= Handler;

                if (args.IsSuccess)
                {
                    navigationCompletion.TrySetResult(true);
                }
                else
                {
                    navigationCompletion.TrySetException(
                        new InvalidOperationException($"Navigation failed with status {args.WebErrorStatus}."));
                }
            }

            ScraperBrowser.NavigationCompleted += Handler;
            ScraperBrowser.Source = new Uri(requestUrl);
            await navigationCompletion.Task;

            string extractedJson = "{}";

            for (int attempt = 0; attempt < MaxDomWaitAttempts; attempt++)
            {
                string rawResult = await ScraperBrowser.ExecuteScriptAsync(CurseForgeExtractionScript);
                extractedJson = JsonConvert.DeserializeObject<string>(rawResult) ?? "{}";
                CurseForgePagePayload interimPayload =
                    JsonConvert.DeserializeObject<CurseForgePagePayload>(extractedJson) ?? new CurseForgePagePayload();

                if ((interimPayload.Items?.Count ?? 0) > 0 || !interimPayload.IsChallengePage)
                {
                    return interimPayload;
                }

                await Task.Delay(250);
            }

            return JsonConvert.DeserializeObject<CurseForgePagePayload>(extractedJson) ?? new CurseForgePagePayload();
        }

        private void ScraperBrowser_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            TryHandleHiddenBrowserPopup(sender, e);
            e.Handled = true;
        }

        private void DownloadBrowser_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            TryHandleHiddenBrowserPopup(sender, e);
            e.Handled = true;
        }

        private void HiddenBrowser_LaunchingExternalUriScheme(object sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
        {
            e.Cancel = true;
            Debug.WriteLine($"Blocked external addon browser launch: {e.Uri}");

            if (pendingDownloadTaskSource != null)
            {
                pendingDownloadTaskSource.TrySetException(
                    new InvalidOperationException(
                        T("AddonsPage_UnsupportedProtocol", "This addon page tried to open an unsupported external protocol instead of giving a normal addon file.")));
            }
        }

        private void HiddenBrowser_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri)) return;

            if (!TryGetUriScheme(e.Uri, out string scheme)) return;
            if (AllowedBrowserSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)) return;

            e.Cancel = true;
            Debug.WriteLine($"Blocked external addon browser protocol: {e.Uri}");

            if (pendingDownloadTaskSource != null)
            {
                pendingDownloadTaskSource.TrySetException(
                    new InvalidOperationException(
                        T("AddonsPage_UnsupportedProtocol", "This addon page tried to open an unsupported external protocol instead of giving a normal addon file.")));
            }
        }

        private void TryHandleHiddenBrowserPopup(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri)) return;

            if (!TryGetUriScheme(e.Uri, out string scheme)) return;

            if (scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (ReferenceEquals(sender, DownloadBrowser.CoreWebView2))
                    {
                        DownloadBrowser.Source = new Uri(e.Uri);
                    }
                    else if (ReferenceEquals(sender, ScraperBrowser.CoreWebView2))
                    {
                        ScraperBrowser.Source = new Uri(e.Uri);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }

                return;
            }

            Debug.WriteLine($"Blocked hidden browser popup protocol: {e.Uri}");

            if (pendingDownloadTaskSource != null)
            {
                pendingDownloadTaskSource.TrySetException(
                    new InvalidOperationException(
                        T("AddonsPage_UnsupportedProtocol", "This addon page tried to open an unsupported external protocol instead of giving a normal addon file.")));
            }
        }

        private void DownloadBrowser_DownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            if (pendingDownloadTaskSource == null) return;

            CoreWebView2Deferral deferral = e.GetDeferral();

            try
            {
                e.Handled = true;

                string suggestedName = Path.GetFileName(e.ResultFilePath);
                if (string.IsNullOrWhiteSpace(suggestedName))
                {
                    suggestedName = Path.GetFileName(new Uri(e.DownloadOperation.Uri).AbsolutePath);
                }

                string targetPath = AddonsCatalogHandler.GetManagedDownloadPath(suggestedName, pendingDownloadTitle);
                e.ResultFilePath = targetPath;

                var operation = e.DownloadOperation;
                EventHandler<object> progressChangedHandler = null;
                EventHandler<object> stateChangedHandler = null;

                void DetachDownloadHandlers()
                {
                    if (progressChangedHandler != null)
                    {
                        operation.BytesReceivedChanged -= progressChangedHandler;
                    }

                    if (stateChangedHandler != null)
                    {
                        operation.StateChanged -= stateChangedHandler;
                    }
                }

                progressChangedHandler = (_, __) =>
                {
                    ulong? rawTotalBytes = operation.TotalBytesToReceive;
                    long totalBytes = rawTotalBytes.HasValue && rawTotalBytes.Value <= (ulong)long.MaxValue
                        ? Convert.ToInt64(rawTotalBytes.Value)
                        : -1;
                    UpdateAddonDownloadProgress(
                        operation.BytesReceived,
                        totalBytes > 0 ? totalBytes : (long?)null);
                };

                stateChangedHandler = async (_, __) =>
                {
                    if (operation.State == CoreWebView2DownloadState.Completed)
                    {
                        DetachDownloadHandlers();

                        bool ready = await WaitUntilFileIsReadyAsync(targetPath);
                        if (ready)
                        {
                            pendingDownloadTaskSource.TrySetResult(targetPath);
                        }
                        else
                        {
                            pendingDownloadTaskSource.TrySetException(
                                new IOException("The addon file was downloaded but could not be opened yet."));
                        }
                    }
                    else if (operation.State == CoreWebView2DownloadState.Interrupted)
                    {
                        DetachDownloadHandlers();
                        pendingDownloadTaskSource.TrySetException(
                            new IOException($"Download interrupted: {operation.InterruptReason}"));
                    }
                };

                operation.BytesReceivedChanged += progressChangedHandler;
                operation.StateChanged += stateChangedHandler;
                progressChangedHandler.Invoke(operation, EventArgs.Empty);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async Task AssistCurseForgeDownloadAsync(int assistSession)
        {
            if (isDownloadAssistRunning || pendingDownloadTaskSource == null)
            {
                return;
            }

            isDownloadAssistRunning = true;

            try
            {
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    if (pendingDownloadTaskSource == null || assistSession != downloadAssistSessionId)
                    {
                        return;
                    }

                    await Task.Delay(attempt == 0 ? 450 : 700);

                    if (pendingDownloadTaskSource == null || DownloadBrowser?.CoreWebView2 == null)
                    {
                        return;
                    }

                    string rawResult = await DownloadBrowser.ExecuteScriptAsync(CurseForgeDownloadAssistScript);
                    string json = JsonConvert.DeserializeObject<string>(rawResult) ?? "{}";
                    CurseForgeDownloadAssistState state =
                        JsonConvert.DeserializeObject<CurseForgeDownloadAssistState>(json) ?? new CurseForgeDownloadAssistState();

                    if (!string.IsNullOrWhiteSpace(state.Url) &&
                        Uri.TryCreate(state.Url, UriKind.Absolute, out Uri nextUri) &&
                        IsSafeCurseForgeAssistUri(nextUri) &&
                        !IsCurrentDownloadBrowserUri(nextUri))
                    {
                        DownloadBrowser.Source = nextUri;
                    }

                    if (state.Clicked)
                    {
                        await Task.Delay(900);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                isDownloadAssistRunning = false;
            }
        }

        private bool IsCurrentDownloadBrowserUri(Uri uri)
        {
            try
            {
                string currentUri = DownloadBrowser?.Source?.AbsoluteUri ?? string.Empty;
                return string.Equals(currentUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSafeCurseForgeAssistUri(Uri uri)
        {
            if (uri == null) return false;

            return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                   (uri.Host.EndsWith("curseforge.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith("forgecdn.net", StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateCatalogLabels()
        {
            string totalText = string.IsNullOrWhiteSpace(totalCountText) ? T("AddonsPage_UnknownTotal", "unknown") : totalCountText;
            ResultsSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("AddonsPage_ResultSummaryFormat", "{0} addons ready"),
                VisibleAddons.Count,
                Math.Max(lastLoadedPage, 0),
                Math.Max(maxKnownPage, 1),
                totalText);
            PageIndicatorText.Text = string.Format(
                CultureInfo.CurrentCulture,
                T("AddonsPage_PageIndicatorFormat", "Page {0} / {1}"),
                Math.Max(lastLoadedPage, 0),
                Math.Max(maxKnownPage, 1));
            LoadMoreButton.Visibility = lastLoadedPage < maxKnownPage && VisibleAddons.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void AddVisibleAddon(AddonEntry addon)
        {
            if (addon == null) return;

            string identity = string.IsNullOrWhiteSpace(addon.PageUri) ? addon.InstallUri : addon.PageUri;
            if (string.IsNullOrWhiteSpace(identity))
            {
                identity = addon.Title;
            }

            if (string.IsNullOrWhiteSpace(identity) || loadedAddonUris.Contains(identity)) return;

            loadedAddonUris.Add(identity);
            VisibleAddons.Add(addon);
        }

        private void FinalizeCatalogPage()
        {
            bool hasHomeFallbackAddon = HasHomeFallbackAddon();
            bool hasCatalogContent = VisibleAddons.Count > 0 || hasHomeFallbackAddon;
            NoResultsState.Visibility = hasCatalogContent ? Visibility.Collapsed : Visibility.Visible;
            if (VisibleAddons.Count > 0)
            {
                emptyCatalogRecoveryAttempts = 0;
            }

            if (!hasCatalogContent)
            {
                NoResultsText.Text = string.IsNullOrWhiteSpace(currentSearchText)
                    ? T("AddonsPage_NoResultsText", "Try another search or reload the list.")
                    : string.Format(CultureInfo.CurrentCulture, T("AddonsPage_NoSearchResults", "Nothing matched \"{0}\"."), currentSearchText);
            }

            RebuildMarketplaceShelves();
            UpdateCatalogLabels();
        }

        private void SetLoadingState(bool isLoading, string message)
        {
            LoadMoreButton.IsEnabled = !isLoading;
        }

        private void RebuildMarketplaceShelves()
        {
            List<AddonEntry> remoteAddons = VisibleAddons.ToList();

            SpotlightAddons.Clear();
            PromoAddons.Clear();
            ShelfOneAddons.Clear();
            ShelfTwoAddons.Clear();

            FeaturedAddon = remoteAddons.FirstOrDefault() ?? bundledActionsAddon;

            foreach (AddonEntry addon in remoteAddons.Skip(1).Take(FeaturedMosaicCount))
            {
                SpotlightAddons.Add(addon);
            }

            foreach (AddonEntry addon in remoteAddons.Skip(1 + FeaturedMosaicCount).Take(PromoAddonsCount))
            {
                PromoAddons.Add(addon);
            }

            bool showBestSellersShelf = bundledActionsAddon != null && string.IsNullOrWhiteSpace(currentSearchText);
            if (BestSellersTitle != null)
            {
                BestSellersTitle.Visibility = showBestSellersShelf ? Visibility.Visible : Visibility.Collapsed;
            }

            if (BestSellersShelfRoot != null)
            {
                BestSellersShelfRoot.Visibility = showBestSellersShelf ? Visibility.Visible : Visibility.Collapsed;
            }

            if (showBestSellersShelf)
            {
                ShelfOneAddons.Add(bundledActionsAddon);
            }

            int bestSellerSkip = 1 + FeaturedMosaicCount + PromoAddonsCount;
            foreach (AddonEntry addon in remoteAddons.Skip(bestSellerSkip))
            {
                ShelfTwoAddons.Add(addon);
            }

            bool hasHomeFallbackAddon = HasHomeFallbackAddon();
            NoResultsState.Visibility = remoteAddons.Count == 0 && !hasHomeFallbackAddon
                ? Visibility.Visible
                : Visibility.Collapsed;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateShelfOneViewport));
            UpdateDetailCollections();
        }

        private bool HasHomeFallbackAddon()
        {
            return bundledActionsAddon != null && string.IsNullOrWhiteSpace(currentSearchText);
        }

        private void QueueEmptyCatalogRecovery(string reason)
        {
            if (isEmptyCatalogRecoveryRunning ||
                emptyCatalogRecoveryAttempts >= MaxEmptyCatalogRecoveryAttempts ||
                VisibleAddons.Count > 0)
            {
                return;
            }

            emptyCatalogRecoveryAttempts++;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
            {
                await RecoverEmptyCatalogAsync(reason);
            }));
        }

        private async Task RecoverEmptyCatalogAsync(string reason)
        {
            if (isEmptyCatalogRecoveryRunning || VisibleAddons.Count > 0)
            {
                return;
            }

            isEmptyCatalogRecoveryRunning = true;

            try
            {
                Debug.WriteLine($"Recovering empty add-ons catalog: {reason}");
                SetLoadingState(true, T("AddonsPage_LoadingText", "Loading addons..."));
                NoResultsState.Visibility = Visibility.Collapsed;

                ResetCatalogBrowserForRecovery();
                await Task.Delay(350);

                if (LoadCachedCatalog() && VisibleAddons.Count > 0)
                {
                    return;
                }

                await ReloadCatalogFromScratchAsync(clearSearch: false);
                if (VisibleAddons.Count > 0)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(currentSearchText) || !string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    SearchBox.Text = string.Empty;
                    currentSearchText = string.Empty;
                    ResetCatalogBrowserForRecovery();
                    await ReloadCatalogFromScratchAsync(clearSearch: true);
                    if (VisibleAddons.Count > 0)
                    {
                        return;
                    }
                }

                RestoreBundledFallbackCatalog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                RestoreBundledFallbackCatalog();
            }
            finally
            {
                isEmptyCatalogRecoveryRunning = false;
                SetLoadingState(false, string.Empty);
            }
        }

        private async Task ReloadCatalogFromScratchAsync(bool clearSearch)
        {
            currentSearchText = clearSearch ? string.Empty : (SearchBox.Text?.Trim() ?? string.Empty);
            lastLoadedPage = 0;
            maxKnownPage = 1;
            totalCountText = string.Empty;
            loadedAddonUris.Clear();
            VisibleAddons.Clear();
            RebuildMarketplaceShelves();
            UpdateCatalogLabels();

            await LoadRemoteAddonsPageAsync(1, reset: true, showLoading: false, allowEmptyRecovery: false);
        }

        private void ResetCatalogBrowserForRecovery()
        {
            hiddenBrowserFailed = false;

            try
            {
                ScraperBrowser?.CoreWebView2?.Reload();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void RestoreBundledFallbackCatalog()
        {
            if (bundledActionsAddon == null)
            {
                return;
            }

            SearchBox.Text = string.Empty;
            currentSearchText = string.Empty;
            VisibleAddons.Clear();
            loadedAddonUris.Clear();
            AddVisibleAddon(bundledActionsAddon);
            totalCountText = VisibleAddons.Count.ToString(CultureInfo.InvariantCulture);
            lastLoadedPage = 1;
            maxKnownPage = 1;
            FinalizeCatalogPage();
        }

        private async Task<string> DownloadAddonPackageAsync(AddonEntry addon)
        {
            await downloadLock.WaitAsync();

            try
            {
                string cachedPackagePath = AddonsCatalogHandler.TryGetCachedDownloadedAddonPackage(addon);
                if (!string.IsNullOrWhiteSpace(cachedPackagePath))
                {
                    SetAddonDownloadState(true, T("AddonsPage_UsingCachedAddon", "Using cached addon package..."));
                    return cachedPackagePath;
                }

                string browserDownloadUri = addon.InstallUri;

                try
                {
                    IProgress<AddonDownloadProgress> progress = new Progress<AddonDownloadProgress>(downloadProgress =>
                    {
                        UpdateAddonDownloadProgress(downloadProgress.BytesReceived, downloadProgress.TotalBytes);
                    });

                    return await AddonsCatalogHandler.DownloadRemotePackageAsync(addon, progress);
                }
                catch (AddonBrowserDownloadRequiredException ex)
                {
                    if (!string.IsNullOrWhiteSpace(ex.BrowserUri))
                    {
                        browserDownloadUri = ex.BrowserUri;
                    }

                    Debug.WriteLine($"Addon download needs WebView: {ex}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Direct addon download failed, falling back to WebView: {ex}");
                }

                if (!await EnsureHiddenDownloadBrowserReadyAsync())
                {
                    throw new InvalidOperationException(T("AddonsPage_DownloadEngineFailed", "The launcher download engine could not start on this PC."));
                }

                pendingDownloadTitle = string.IsNullOrWhiteSpace(addon.Title) ? "addon" : addon.Title;
                pendingDownloadTaskSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                int assistSession = Interlocked.Increment(ref downloadAssistSessionId);

                SetAddonDownloadState(true, string.Format(CultureInfo.CurrentCulture, T("AddonsPage_DownloadingFormat", "Downloading {0}..."), addon.Title));
                DownloadBrowser.Source = new Uri(browserDownloadUri);
                _ = AssistCurseForgeDownloadAsync(assistSession);

                using CancellationTokenSource timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                using CancellationTokenRegistration timeoutRegistration = timeoutSource.Token.Register(() =>
                {
                    pendingDownloadTaskSource.TrySetException(
                        new TimeoutException(T("AddonsPage_DownloadTimeout", "The launcher took too long to start the addon download.")));
                });

                return await pendingDownloadTaskSource.Task;
            }
            finally
            {
                pendingDownloadTitle = string.Empty;
                pendingDownloadTaskSource = null;
                downloadLock.Release();
            }
        }

        private void StartDownloadsWatcher()
        {
            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath)) return;

            StopDownloadsWatcher();
            importTriggered = 0;
            downloadsWatcherToken = new CancellationTokenSource();
            downloadsWatcher = new FileSystemWatcher(downloadsPath)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            downloadsWatcher.Created += DownloadsWatcher_OnCandidate;
            downloadsWatcher.Renamed += DownloadsWatcher_OnRenameCandidate;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), downloadsWatcherToken.Token);
                }
                catch (TaskCanceledException)
                {
                }

                StopDownloadsWatcher();
            });
        }

        private void StopDownloadsWatcher()
        {
            downloadsWatcherToken?.Cancel();
            downloadsWatcherToken?.Dispose();
            downloadsWatcherToken = null;

            if (downloadsWatcher == null) return;

            downloadsWatcher.EnableRaisingEvents = false;
            downloadsWatcher.Created -= DownloadsWatcher_OnCandidate;
            downloadsWatcher.Renamed -= DownloadsWatcher_OnRenameCandidate;
            downloadsWatcher.Dispose();
            downloadsWatcher = null;
        }

        private void DownloadsWatcher_OnCandidate(object sender, FileSystemEventArgs e)
        {
            _ = HandleDownloadedAddonCandidateAsync(e.FullPath);
        }

        private void DownloadsWatcher_OnRenameCandidate(object sender, RenamedEventArgs e)
        {
            _ = HandleDownloadedAddonCandidateAsync(e.FullPath);
        }

        private async Task HandleDownloadedAddonCandidateAsync(string fullPath)
        {
            string extension = Path.GetExtension(fullPath);
            if (!extension.Equals(".mcpack", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".mcaddon", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref importTriggered, 1, 0) != 0) return;

            bool ready = await WaitUntilFileIsReadyAsync(fullPath);
            if (!ready)
            {
                importTriggered = 0;
                return;
            }

            await OpenAddonFileAsync(fullPath);
            StopDownloadsWatcher();
            StartDownloadsWatcher();
        }

        private static async Task<bool> WaitUntilFileIsReadyAsync(string filePath)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    return stream.Length > 0;
                }
                catch (IOException)
                {
                    await Task.Delay(500);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(500);
                }
            }

            return false;
        }

        private static async Task OpenAddonFileAsync(string addonPath)
        {
            if (!File.Exists(addonPath))
            {
                MessageBox.Show(
                    string.Format(CultureInfo.CurrentCulture, T("AddonsPage_FileMissing", "Addon file not found:\n{0}"), addonPath),
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                bool opened = await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    StorageFile addonFile = await StorageFile.GetFileFromPathAsync(addonPath);
                    return await Launcher.LaunchFileAsync(addonFile);
                }).Task.Unwrap();

                if (opened)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = addonPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(CultureInfo.CurrentCulture, T("AddonsPage_OpenFailed", "The addon downloaded, but Windows couldn't open it.\n{0}"), ex.Message),
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool TryGetUriScheme(string rawUri, out string scheme)
        {
            scheme = string.Empty;

            if (!Uri.TryCreate(rawUri, UriKind.Absolute, out Uri parsedUri))
            {
                return false;
            }

            scheme = parsedUri.Scheme;
            return true;
        }

        private static string T(string key, string fallback)
        {
            return LanguageManager.GetResource(key) as string ?? fallback;
        }

        private void StartOverlaySpinner()
        {
            DoubleAnimation rotationAnimation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(760)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            OverlaySpinnerRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotationAnimation);
        }

        private async Task ShowAddonsOverlayAsync(Func<Task> work, TimeSpan minimumDuration)
        {
            if (work == null) return;

            int currentSession = Interlocked.Increment(ref overlaySessionId);
            isAddonsOverlayBusy = true;
            SetAddonsOverlayVisible(true);

            Task waitTask = Task.Delay(minimumDuration);
            Exception capturedException = null;

            try
            {
                await Task.WhenAll(work(), waitTask);
            }
            catch (Exception ex)
            {
                capturedException = ex;
                await waitTask;
            }
            finally
            {
                if (currentSession == overlaySessionId)
                {
                    SetAddonsOverlayVisible(false);
                    isAddonsOverlayBusy = false;
                }
            }

            if (capturedException != null)
            {
                throw capturedException;
            }
        }

        private void SetAddonsOverlayVisible(bool isVisible)
        {
            if (AddonsLoadingOverlay == null) return;

            AddonsLoadingOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            AddonsLoadingOverlay.IsHitTestVisible = isVisible;
        }

        private bool LoadCachedCatalog()
        {
            List<AddonEntry> cachedAddons = AddonsCatalogHandler.LoadCachedRemoteAddons();
            if (cachedAddons.Count == 0) return false;

            VisibleAddons.Clear();
            loadedAddonUris.Clear();

            foreach (AddonEntry addon in cachedAddons)
            {
                VisibleAddons.Add(addon);

                if (!string.IsNullOrWhiteSpace(addon.PageUri))
                {
                    loadedAddonUris.Add(addon.PageUri);
                }
            }

            totalCountText = cachedAddons.Count.ToString(CultureInfo.InvariantCulture);
            lastLoadedPage = 1;
            maxKnownPage = 1;
            RebuildMarketplaceShelves();
            UpdateCatalogLabels();
            return true;
        }

        private void OpenAddonDetails(AddonEntry addon)
        {
            if (addon == null) return;

            SetAddonDownloadState(false, string.Empty);
            SelectedAddon = addon;
            CurrentHeaderTitle = addon.Title;
            CatalogViewRoot.Visibility = Visibility.Collapsed;
            DetailViewRoot.Visibility = Visibility.Visible;
            HeaderBackButton.Visibility = Visibility.Visible;
            UpdateDetailCollections();
        }

        private void ShowCatalogView()
        {
            SetAddonDownloadState(false, string.Empty);
            CurrentHeaderTitle = T("AddonsPage_MarketplaceTitle", "Addons");
            CatalogViewRoot.Visibility = Visibility.Visible;
            DetailViewRoot.Visibility = Visibility.Collapsed;
            HeaderBackButton.Visibility = Visibility.Collapsed;
        }

        private void SetAddonDownloadState(bool isBusy, string message)
        {
            isAddonDownloadBusy = isBusy;

            if (DownloadAddonButton != null)
            {
                DownloadAddonButton.IsEnabled = !isBusy;
                DownloadAddonButton.Opacity = isBusy ? 0.78 : 1;
            }

            if (DownloadLoadingBar != null)
            {
                DownloadLoadingBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
                DownloadLoadingBar.Minimum = 0;
                DownloadLoadingBar.Maximum = 100;
                DownloadLoadingBar.Value = 0;
                DownloadLoadingBar.IsIndeterminate = isBusy;
            }

            if (DownloadStatusText != null)
            {
                DownloadStatusText.Text = message;
                DownloadStatusText.Visibility = isBusy && !string.IsNullOrWhiteSpace(message)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void UpdateAddonDownloadProgress(long bytesReceived, long? totalBytes)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    UpdateAddonDownloadProgress(bytesReceived, totalBytes)));
                return;
            }

            if (!isAddonDownloadBusy) return;

            if (DownloadLoadingBar != null)
            {
                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    double percent = Math.Max(0, Math.Min(100, bytesReceived * 100d / totalBytes.Value));
                    DownloadLoadingBar.IsIndeterminate = false;
                    DownloadLoadingBar.Value = percent;
                }
                else
                {
                    DownloadLoadingBar.IsIndeterminate = true;
                }
            }

            if (DownloadStatusText != null && bytesReceived > 0)
            {
                string progressText = totalBytes.HasValue && totalBytes.Value > 0
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        T("AddonsPage_DownloadProgressFormat", "Downloading... {0} / {1}"),
                        FormatDownloadBytes(bytesReceived),
                        FormatDownloadBytes(totalBytes.Value))
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        T("AddonsPage_DownloadReceivedFormat", "Downloading... {0} received"),
                        FormatDownloadBytes(bytesReceived));

                DownloadStatusText.Text = progressText;
                DownloadStatusText.Visibility = Visibility.Visible;
            }
        }

        private static string FormatDownloadBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = Math.Max(0, bytes);
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0} {1}", value, units[unitIndex])
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", value, units[unitIndex]);
        }

        private void UpdateDetailCollections()
        {
            RelatedAddons.Clear();
            CreatorAddons.Clear();

            if (SelectedAddon == null) return;

            List<AddonEntry> pool = VisibleAddons
                .Where(x => x != null && !string.Equals(x.Title, SelectedAddon.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (AddonEntry addon in pool.Take(4))
            {
                RelatedAddons.Add(addon);
            }

            List<AddonEntry> sameCreator = pool
                .Where(x => !string.IsNullOrWhiteSpace(SelectedAddon.Author) &&
                            string.Equals(x.Author, SelectedAddon.Author, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList();

            if (sameCreator.Count == 0)
            {
                sameCreator = pool.Skip(4).Take(4).ToList();
            }

            foreach (AddonEntry addon in sameCreator)
            {
                CreatorAddons.Add(addon);
            }
        }

        private void ScrollShelfOneBy(double delta)
        {
            if (ShelfOneScrollViewer == null) return;

            double maxOffset = Math.Max(0, ShelfOneScrollViewer.ExtentWidth - ShelfOneScrollViewer.ViewportWidth);
            double targetOffset = Math.Max(0, Math.Min(maxOffset, ShelfOneScrollViewer.HorizontalOffset + delta));

            AnimateShelfOneTo(targetOffset);
        }

        private double GetShelfScrollStep()
        {
            if (ShelfOneScrollViewer == null || ShelfOneScrollViewer.ViewportWidth <= 0)
            {
                return 540;
            }

            return Math.Max(320, ShelfOneScrollViewer.ViewportWidth - 120);
        }

        private void AnimateShelfOneTo(double targetOffset)
        {
            if (ShelfOneScrollViewer == null) return;

            shelfOneScrollTimer ??= new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            shelfOneScrollTimer.Tick -= ShelfOneScrollTimer_Tick;
            shelfOneScrollTimer.Tick += ShelfOneScrollTimer_Tick;

            shelfOneAnimationFrom = ShelfOneScrollViewer.HorizontalOffset;
            shelfOneAnimationTo = targetOffset;
            shelfOneAnimationStartedAt = DateTime.UtcNow;

            shelfOneScrollTimer.Stop();
            shelfOneScrollTimer.Start();
        }

        private void UpdateShelfOneViewport()
        {
            if (ShelfOneScrollViewer == null) return;

            if (ShelfOneScrollViewer.HorizontalOffset > Math.Max(0, ShelfOneScrollViewer.ExtentWidth - ShelfOneScrollViewer.ViewportWidth))
            {
                ShelfOneScrollViewer.ScrollToHorizontalOffset(0);
            }

            UpdateShelfOneScrollButtons();
        }

        private void ShelfOneScrollTimer_Tick(object sender, EventArgs e)
        {
            if (ShelfOneScrollViewer == null || shelfOneScrollTimer == null)
            {
                shelfOneScrollTimer?.Stop();
                return;
            }

            double progress = (DateTime.UtcNow - shelfOneAnimationStartedAt).TotalMilliseconds / ShelfAnimationDuration.TotalMilliseconds;
            progress = Math.Max(0, Math.Min(1, progress));

            double easedProgress = 1 - Math.Pow(1 - progress, 3);
            double currentOffset = shelfOneAnimationFrom + ((shelfOneAnimationTo - shelfOneAnimationFrom) * easedProgress);
            ShelfOneScrollViewer.ScrollToHorizontalOffset(currentOffset);

            if (progress >= 1)
            {
                shelfOneScrollTimer.Stop();
                ShelfOneScrollViewer.ScrollToHorizontalOffset(shelfOneAnimationTo);
                UpdateShelfOneScrollButtons();
            }
        }

        private void UpdateShelfOneScrollButtons()
        {
            if (ShelfOneScrollViewer == null || ShelfOneLeftButton == null || ShelfOneRightButton == null) return;

            bool canScroll = ShelfOneScrollViewer.ExtentWidth > ShelfOneScrollViewer.ViewportWidth + ShelfScrollPadding;
            bool canScrollLeft = canScroll && ShelfOneScrollViewer.HorizontalOffset > ShelfScrollPadding;
            bool canScrollRight = canScroll && ShelfOneScrollViewer.HorizontalOffset < (ShelfOneScrollViewer.ExtentWidth - ShelfOneScrollViewer.ViewportWidth - ShelfScrollPadding);

            ShelfOneLeftButton.IsEnabled = canScrollLeft;
            ShelfOneLeftButton.Opacity = canScrollLeft ? 1 : 0.45;
            ShelfOneRightButton.IsEnabled = canScrollRight;
            ShelfOneRightButton.Opacity = canScrollRight ? 1 : 0.45;
        }

        private ScrollViewer GetActiveScrollViewer()
        {
            if (DetailViewRoot.Visibility == Visibility.Visible)
            {
                return DetailScrollViewer;
            }

            return CatalogScrollViewer;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class CurseForgePagePayload
        {
            [JsonProperty("isChallengePage")]
            public bool IsChallengePage { get; set; }

            [JsonProperty("totalCountText")]
            public string TotalCountText { get; set; } = string.Empty;

            [JsonProperty("currentPage")]
            public int CurrentPage { get; set; }

            [JsonProperty("maxKnownPage")]
            public int MaxKnownPage { get; set; }

            [JsonProperty("items")]
            public List<CurseForgePageItem> Items { get; set; } = new List<CurseForgePageItem>();
        }

        private sealed class CurseForgePageItem
        {
            [JsonProperty("title")]
            public string Title { get; set; } = string.Empty;

            [JsonProperty("author")]
            public string Author { get; set; } = string.Empty;

            [JsonProperty("description")]
            public string Description { get; set; } = string.Empty;

            [JsonProperty("imagePath")]
            public string ImagePath { get; set; } = string.Empty;

            [JsonProperty("installUri")]
            public string InstallUri { get; set; } = string.Empty;

            [JsonProperty("pageUri")]
            public string PageUri { get; set; } = string.Empty;

            [JsonProperty("downloadsText")]
            public string DownloadsText { get; set; } = string.Empty;

            [JsonProperty("updatedText")]
            public string UpdatedText { get; set; } = string.Empty;

            [JsonProperty("fileSizeText")]
            public string FileSizeText { get; set; } = string.Empty;

            [JsonProperty("gameVersionText")]
            public string GameVersionText { get; set; } = string.Empty;

            [JsonProperty("sourceLabel")]
            public string SourceLabel { get; set; } = string.Empty;
        }

        private sealed class CurseForgeDownloadAssistState
        {
            [JsonProperty("clicked")]
            public bool Clicked { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; } = string.Empty;

            [JsonProperty("reason")]
            public string Reason { get; set; } = string.Empty;
        }
    }
}
