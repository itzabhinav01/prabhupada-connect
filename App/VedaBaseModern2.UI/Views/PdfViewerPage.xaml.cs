using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using VedaBaseModern.Core.Models;
using VedaBaseModern.UI.Services;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed class PdfPageItem : INotifyPropertyChanged
    {
        public uint PageIndex { get; init; }
        public int PageNumber => (int)PageIndex + 1;
        public string PageLabel => $"Loading Page {PageNumber}...";

        public double NativeWidth { get; set; } = 780.0;
        public double NativeHeight { get; set; } = 1080.0;

        private double _displayWidth = 780;
        public double DisplayWidth
        {
            get => _displayWidth;
            set { if (Math.Abs(_displayWidth - value) > 0.1) { _displayWidth = value; OnPropertyChanged(); } }
        }

        private double _displayHeight = 1080;
        public double DisplayHeight
        {
            get => _displayHeight;
            set { if (Math.Abs(_displayHeight - value) > 0.1) { _displayHeight = value; OnPropertyChanged(); } }
        }

        private double _unrotatedWidth = 780;
        public double UnrotatedWidth
        {
            get => _unrotatedWidth;
            set { if (Math.Abs(_unrotatedWidth - value) > 0.1) { _unrotatedWidth = value; OnPropertyChanged(); } }
        }

        private double _unrotatedHeight = 1080;
        public double UnrotatedHeight
        {
            get => _unrotatedHeight;
            set { if (Math.Abs(_unrotatedHeight - value) > 0.1) { _unrotatedHeight = value; OnPropertyChanged(); } }
        }

        private double _imageCanvasLeft;
        public double ImageCanvasLeft
        {
            get => _imageCanvasLeft;
            set { if (Math.Abs(_imageCanvasLeft - value) > 0.1) { _imageCanvasLeft = value; OnPropertyChanged(); } }
        }

        private double _imageCanvasTop;
        public double ImageCanvasTop
        {
            get => _imageCanvasTop;
            set { if (Math.Abs(_imageCanvasTop - value) > 0.1) { _imageCanvasTop = value; OnPropertyChanged(); } }
        }

        private double _rotationAngle;
        public double RotationAngle
        {
            get => _rotationAngle;
            set { if (Math.Abs(_rotationAngle - value) > 0.1) { _rotationAngle = value; OnPropertyChanged(); } }
        }

        private BitmapImage? _imageSource;
        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set { if (!ReferenceEquals(_imageSource, value)) { _imageSource = value; OnPropertyChanged(); } }
        }

        public double RenderedZoom { get; set; }
        public bool IsRendering { get; set; }

        public void ApplyTransformAndSize(double zoomFactor, int rotationSteps)
        {
            bool isSideways = (rotationSteps % 2) != 0;
            double effW = isSideways ? NativeHeight : NativeWidth;
            double effH = isSideways ? NativeWidth : NativeHeight;
            double aspect = effW > 0 ? effH / effW : 1.38;

            double baseW = (effW > effH) ? 940.0 : 780.0;
            double dispW = Math.Round(baseW * zoomFactor);
            double dispH = Math.Round(dispW * aspect);

            double unrotW = isSideways ? dispH : dispW;
            double unrotH = isSideways ? dispW : dispH;

            DisplayWidth = dispW;
            DisplayHeight = dispH;
            UnrotatedWidth = unrotW;
            UnrotatedHeight = unrotH;
            ImageCanvasLeft = (dispW - unrotW) / 2.0;
            ImageCanvasTop = (dispH - unrotH) / 2.0;
            RotationAngle = (rotationSteps & 3) * 90.0;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed partial class PdfViewerPage : Page
    {
        private static readonly Dictionary<string, int> _savedRotations = new(StringComparer.OrdinalIgnoreCase);

        private BookNode? _currentBook;
        private string? _pdfPath;
        private string? _loadedPdfPath;
        private PdfDocument? _pdfDocument;
        private readonly ObservableCollection<PdfPageItem> _pages = new();
        private readonly SemaphoreSlim _renderLock = new(2, 2);

        private double _nativePageWidth = 780.0;
        private double _nativePageHeight = 1080.0;
        private double _basePageWidth = 780.0;
        private double _basePageHeight = 1080.0;
        private double _zoomFactor = 1.0;
        private int _rotationSteps; // 0 = 0°, 1 = 90° CW, 2 = 180°, 3 = 270° CW
        private int _currentPageIndex;
        private bool _isZenMode;
        private bool _suppressViewChanged;
        private ScrollViewer? _listScrollViewer;

        public PdfViewerPage()
        {
            this.NavigationCacheMode = NavigationCacheMode.Enabled;
            this.InitializeComponent();
            this.Loaded += PdfViewerPage_Loaded;
        }

        private void PdfViewerPage_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureScrollViewerHooked();
        }

        private void EnsureScrollViewerHooked()
        {
            if (_listScrollViewer != null) return;
            _listScrollViewer = FindDescendant<ScrollViewer>(PagesListView);
            if (_listScrollViewer != null)
            {
                _listScrollViewer.ViewChanged -= ListScrollViewer_ViewChanged;
                _listScrollViewer.ViewChanged += ListScrollViewer_ViewChanged;
            }
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is BookNode book)
            {
                _currentBook = book;
                _pdfPath = book.PdfPath;
                BookTitleText.Text = book.Title;
                AuthorText.Text = book.Author;
            }
            else if (e.Parameter is string path && File.Exists(path))
            {
                _pdfPath = path;
                BookTitleText.Text = Path.GetFileNameWithoutExtension(path);
                AuthorText.Text = "PDF Document";
            }

            if (!string.IsNullOrWhiteSpace(_pdfPath) && File.Exists(_pdfPath))
            {
                if (!string.Equals(_loadedPdfPath, _pdfPath, StringComparison.OrdinalIgnoreCase) || _pdfDocument == null)
                {
                    await LoadPdfDocumentAsync(_pdfPath);
                }
                else
                {
                    LoadingRing.IsActive = false;
                    QueueVisibleRangeRender();
                }
            }
            else
            {
                LoadingRing.IsActive = false;
                BookTitleText.Text = "PDF file not found";
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_isZenMode)
            {
                ExitZenMode();
            }
        }

        private void RecalculateBaseDimensions()
        {
            bool isRotatedSideways = (_rotationSteps % 2) != 0;
            double effW = isRotatedSideways ? _nativePageHeight : _nativePageWidth;
            double effH = isRotatedSideways ? _nativePageWidth : _nativePageHeight;
            double aspect = effW > 0 ? effH / effW : 1.38;
            _basePageWidth = effW > effH ? 940.0 : 780.0;
            _basePageHeight = Math.Round(_basePageWidth * aspect);
        }

        private async Task LoadPdfDocumentAsync(string pdfPath)
        {
            try
            {
                LoadingRing.IsActive = true;
                _pages.Clear();

                var storageFile = await StorageFile.GetFileFromPathAsync(pdfPath);
                _pdfDocument = await PdfDocument.LoadFromFileAsync(storageFile);
                _loadedPdfPath = pdfPath;
                _rotationSteps = _savedRotations.TryGetValue(pdfPath, out int savedRot) ? (savedRot & 3) : 0;

                uint pageCount = _pdfDocument.PageCount;
                TotalPagesText.Text = $"/ {pageCount}";
                PageJumpBox.Text = pageCount > 0 ? "1" : "0";
                FocusPageText.Text = pageCount > 0 ? $"1 / {pageCount}" : "0 / 0";
                _currentPageIndex = 0;

                if (pageCount > 0)
                {
                    // Sample page 1 (or page 0 if single page) in case page 0 is a portrait cover and body pages are spreads
                    uint sampleIndex = pageCount > 2 ? 1u : 0u;
                    using var samplePage = _pdfDocument.GetPage(sampleIndex);
                    var size = samplePage.Size;
                    _nativePageWidth = size.Width > 0 ? size.Width : 780.0;
                    _nativePageHeight = size.Height > 0 ? size.Height : 1080.0;
                    RecalculateBaseDimensions();
                }

                for (uint i = 0; i < pageCount; i++)
                {
                    var item = new PdfPageItem
                    {
                        PageIndex = i,
                        NativeWidth = _nativePageWidth,
                        NativeHeight = _nativePageHeight
                    };
                    item.ApplyTransformAndSize(_zoomFactor, _rotationSteps);
                    _pages.Add(item);
                }

                PagesListView.ItemsSource = _pages;
                LoadingRing.IsActive = false;

                EnsureScrollViewerHooked();

                // Pre-render the first 4 pages immediately for instant display
                uint initialCount = Math.Min(pageCount, 4u);
                for (uint i = 0; i < initialCount; i++)
                {
                    _ = RenderPageItemAsync(_pages[(int)i], forceRender: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfViewerPage] Failed to load PDF: {ex}");
                LoadingRing.IsActive = false;
            }
        }

        private (int FirstIndex, int LastIndex) GetVisiblePageRange()
        {
            if (_pages.Count == 0) return (0, 0);

            if (PagesListView?.ItemsPanelRoot is ItemsStackPanel stackPanel &&
                stackPanel.FirstVisibleIndex >= 0 &&
                stackPanel.LastVisibleIndex >= stackPanel.FirstVisibleIndex)
            {
                return (
                    Math.Clamp(stackPanel.FirstVisibleIndex, 0, _pages.Count - 1),
                    Math.Clamp(stackPanel.LastVisibleIndex, 0, _pages.Count - 1)
                );
            }

            int fallback = Math.Clamp(_currentPageIndex, 0, _pages.Count - 1);
            return (fallback, Math.Min(_pages.Count - 1, fallback + 1));
        }

        private void QueueVisibleRangeRender()
        {
            if (_pages.Count == 0) return;
            var (first, last) = GetVisiblePageRange();

            // Render visible pages first with highest priority
            for (int i = first; i <= last; i++)
            {
                _ = RenderPageItemAsync(_pages[i], forceRender: true);
            }

            // Pre-render 2 pages ahead and 1 page behind
            for (int i = Math.Max(0, first - 1); i <= Math.Min(_pages.Count - 1, last + 2); i++)
            {
                if (i < first || i > last)
                {
                    _ = RenderPageItemAsync(_pages[i], forceRender: false);
                }
            }
        }

        private void PagesListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            EnsureScrollViewerHooked();

            if (args.Item is not PdfPageItem item) return;
            if (args.InRecycleQueue) return;

            _ = RenderPageItemAsync(item, forceRender: true);
        }

        private async Task RenderPageItemAsync(PdfPageItem item, bool forceRender = false)
        {
            if (_pdfDocument == null) return;
            if (item.ImageSource != null && Math.Abs(item.RenderedZoom - _zoomFactor) < 0.05) return;
            if (item.IsRendering) return;

            item.IsRendering = true;
            try
            {
                await _renderLock.WaitAsync();
                try
                {
                    if (_pdfDocument == null) return;
                    double targetZoom = _zoomFactor;
                    if (item.ImageSource != null && Math.Abs(item.RenderedZoom - targetZoom) < 0.05) return;

                    var (firstVis, lastVis) = GetVisiblePageRange();
                    bool isNearVisible = ((int)item.PageIndex >= firstVis - 2 && (int)item.PageIndex <= lastVis + 3) ||
                                         Math.Abs((int)item.PageIndex - _currentPageIndex) <= 4;

                    if (!forceRender && !isNearVisible)
                    {
                        return;
                    }

                    using var pdfPage = _pdfDocument.GetPage(item.PageIndex);
                    var pageSize = pdfPage.Size;
                    if (pageSize.Width > 0 && pageSize.Height > 0)
                    {
                        item.NativeWidth = pageSize.Width;
                        item.NativeHeight = pageSize.Height;
                        item.ApplyTransformAndSize(targetZoom, _rotationSteps);
                    }

                    using var stream = new InMemoryRandomAccessStream();
                    var renderOptions = new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)Math.Clamp(item.UnrotatedWidth * 1.45, 650, 1900),
                        BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.BmpEncoderId
                    };
                    await pdfPage.RenderToStreamAsync(stream, renderOptions);
                    stream.Seek(0);

                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);

                    item.ImageSource = bitmap;
                    item.RenderedZoom = targetZoom;

                    EvictDistantPages(firstVis, lastVis);
                }
                finally
                {
                    _renderLock.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfViewerPage] Page render error: {ex.Message}");
            }
            finally
            {
                item.IsRendering = false;
            }
        }

        private void EvictDistantPages(int firstVis, int lastVis)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                if (_pages[i].ImageSource != null && (i < firstVis - 10 || i > lastVis + 10) && Math.Abs(i - _currentPageIndex) > 10)
                {
                    _pages[i].ImageSource = null;
                    _pages[i].RenderedZoom = 0;
                }
            }
        }

        private void RotatePages(int deltaSteps)
        {
            if (_pages.Count == 0) return;

            int savedPageIndex = Math.Clamp(_currentPageIndex, 0, _pages.Count - 1);
            _rotationSteps = ((_rotationSteps + deltaSteps) % 4 + 4) % 4;
            if (!string.IsNullOrWhiteSpace(_loadedPdfPath))
            {
                _savedRotations[_loadedPdfPath] = _rotationSteps;
            }

            _suppressViewChanged = true;
            try
            {
                RecalculateBaseDimensions();

                // Rotate in-place on GPU without clearing ImageSource!
                foreach (var p in _pages)
                {
                    p.ApplyTransformAndSize(_zoomFactor, _rotationSteps);
                }

                PagesListView.UpdateLayout();
                PagesListView.ScrollIntoView(_pages[savedPageIndex], ScrollIntoViewAlignment.Leading);
                PagesListView.UpdateLayout();

                _currentPageIndex = savedPageIndex;
                PageJumpBox.Text = (_currentPageIndex + 1).ToString();
                FocusPageText.Text = $"{_currentPageIndex + 1} / {_pages.Count}";
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _suppressViewChanged = false;
                    QueueVisibleRangeRender();
                });
            }
        }

        private void RotateRightButton_Click(object sender, RoutedEventArgs e) => RotatePages(1);
        private void RotateLeftButton_Click(object sender, RoutedEventArgs e) => RotatePages(-1);

        private void RotateRightAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            RotatePages(1);
            args.Handled = true;
        }

        private void RotateLeftAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            RotatePages(-1);
            args.Handled = true;
        }

        private void ListScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_suppressViewChanged || _listScrollViewer == null || _pages.Count == 0) return;

            int detectedIndex = _currentPageIndex;
            if (PagesListView?.ItemsPanelRoot is ItemsStackPanel stackPanel && stackPanel.FirstVisibleIndex >= 0)
            {
                detectedIndex = Math.Clamp(stackPanel.FirstVisibleIndex, 0, _pages.Count - 1);
            }
            else
            {
                double pageStep = (_basePageHeight * _zoomFactor) + 18.0;
                if (pageStep > 1)
                {
                    detectedIndex = (int)Math.Clamp(Math.Floor((_listScrollViewer.VerticalOffset + (pageStep * 0.35)) / pageStep), 0, _pages.Count - 1);
                }
            }

            if (detectedIndex != _currentPageIndex)
            {
                _currentPageIndex = detectedIndex;
                PageJumpBox.Text = (_currentPageIndex + 1).ToString();
                FocusPageText.Text = $"{_currentPageIndex + 1} / {_pages.Count}";
            }

            if (!e.IsIntermediate)
            {
                QueueVisibleRangeRender();
            }
            else
            {
                _ = RenderPageItemAsync(_pages[_currentPageIndex], forceRender: true);
            }
        }

        private void ScrollToPageIndex(int index)
        {
            if (_pages.Count == 0) return;
            int clamped = Math.Clamp(index, 0, _pages.Count - 1);

            _suppressViewChanged = true;
            try
            {
                _currentPageIndex = clamped;
                PageJumpBox.Text = (clamped + 1).ToString();
                FocusPageText.Text = $"{clamped + 1} / {_pages.Count}";

                PagesListView.ScrollIntoView(_pages[clamped], ScrollIntoViewAlignment.Leading);
                PagesListView.UpdateLayout();
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _suppressViewChanged = false;
                    _ = RenderPageItemAsync(_pages[clamped], forceRender: true);
                    if (clamped + 1 < _pages.Count)
                    {
                        _ = RenderPageItemAsync(_pages[clamped + 1], forceRender: true);
                    }
                    QueueVisibleRangeRender();
                });
            }
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex > 0)
            {
                ScrollToPageIndex(_currentPageIndex - 1);
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex < _pages.Count - 1)
            {
                ScrollToPageIndex(_currentPageIndex + 1);
            }
        }

        private void PageJumpBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (int.TryParse(PageJumpBox.Text.Trim(), out int pageNum))
                {
                    ScrollToPageIndex(pageNum - 1);
                }
                else
                {
                    PageJumpBox.Text = (_currentPageIndex + 1).ToString();
                }
                e.Handled = true;
            }
        }

        private void SetZoomFactor(double newZoom)
        {
            double clamped = Math.Clamp(Math.Round(newZoom, 2), 0.5, 2.5);
            if (Math.Abs(clamped - _zoomFactor) < 0.01) return;

            int savedPageIndex = Math.Clamp(_currentPageIndex, 0, Math.Max(0, _pages.Count - 1));
            _zoomFactor = clamped;
            ZoomPercentText.Text = $"{Math.Round(_zoomFactor * 100)}%";

            _suppressViewChanged = true;
            try
            {
                foreach (var p in _pages)
                {
                    p.ApplyTransformAndSize(_zoomFactor, _rotationSteps);
                }

                PagesListView.UpdateLayout();
                if (_pages.Count > 0)
                {
                    PagesListView.ScrollIntoView(_pages[savedPageIndex], ScrollIntoViewAlignment.Leading);
                    PagesListView.UpdateLayout();
                }

                _currentPageIndex = savedPageIndex;
                PageJumpBox.Text = (_currentPageIndex + 1).ToString();
                FocusPageText.Text = $"{_currentPageIndex + 1} / {_pages.Count}";
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _suppressViewChanged = false;
                    QueueVisibleRangeRender();
                });
            }
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => SetZoomFactor(_zoomFactor + 0.15);
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => SetZoomFactor(_zoomFactor - 0.15);
        private void ZoomResetButton_Click(object sender, RoutedEventArgs e) => SetZoomFactor(1.0);

        private void ZoomInAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomFactor(_zoomFactor + 0.15);
            args.Handled = true;
        }

        private void ZoomOutAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomFactor(_zoomFactor - 0.15);
            args.Handled = true;
        }

        private void ZoomResetAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SetZoomFactor(1.0);
            args.Handled = true;
        }

        private void PagesListView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            if ((ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down)
            {
                var delta = e.GetCurrentPoint(PagesListView).Properties.MouseWheelDelta;
                if (delta > 0) SetZoomFactor(_zoomFactor + 0.1);
                else if (delta < 0) SetZoomFactor(_zoomFactor - 0.1);
                e.Handled = true;
            }
        }

        private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
        {
            MainPage.Current?.ToggleLibraryPane();
        }

        private void ZenModeToggleButton_Click(object sender, RoutedEventArgs e) => ToggleZenMode();

        private void ZenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            ToggleZenMode();
            args.Handled = true;
        }

        private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (_isZenMode)
            {
                ExitZenMode();
                args.Handled = true;
            }
        }

        public void ToggleZenMode(bool? forceZen = null)
        {
            int savedPageIndex = Math.Clamp(_currentPageIndex, 0, Math.Max(0, _pages.Count - 1));
            _isZenMode = forceZen ?? !_isZenMode;

            _suppressViewChanged = true;
            try
            {
                if (TopNavGrid != null)
                {
                    TopNavGrid.Visibility = _isZenMode ? Visibility.Collapsed : Visibility.Visible;
                }
                if (FloatingZenExitButton != null)
                {
                    FloatingZenExitButton.Visibility = _isZenMode ? Visibility.Visible : Visibility.Collapsed;
                }
                if (ZenModeIcon != null && ZenModeToggleButton != null)
                {
                    ZenModeIcon.Glyph = _isZenMode ? "\uE73F" : "\uE740";
                    ToolTipService.SetToolTip(ZenModeToggleButton, _isZenMode ? "Exit Focus View (Esc)" : "Focus View (F11)");
                }

                if (_isZenMode)
                {
                    MainPage.Current?.EnterFocusMode(this);
                }
                else
                {
                    MainPage.Current?.ExitFocusMode();
                }

                if (PagesListView != null)
                {
                    PagesListView.Visibility = Visibility.Visible;
                    PagesListView.MaxHeight = double.PositiveInfinity;
                    PagesListView.UpdateLayout();
                    if (_pages.Count > 0)
                    {
                        PagesListView.ScrollIntoView(_pages[savedPageIndex], ScrollIntoViewAlignment.Leading);
                        PagesListView.UpdateLayout();
                    }
                }
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _suppressViewChanged = false;
                    QueueVisibleRangeRender();
                });
            }
        }

        public void ExitZenMode() => ToggleZenMode(false);

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }

        private async void RemoveBookButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBook == null) return;

            var dialog = new ContentDialog
            {
                Title = "Remove PDF Book",
                Content = $"Are you sure you want to remove '{_currentBook.Title}' from your Library? This will remove the book and its file.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (_isZenMode)
                {
                    ExitZenMode();
                }

                if (App.Current.BookImportService != null)
                {
                    await App.Current.BookImportService.DeleteBookAsync(_currentBook.BookKey);
                }

                if (MainPage.Current != null)
                {
                    await MainPage.Current.ViewModel.LoadBooksAsync(true);
                }

                if (this.Frame.CanGoBack)
                {
                    this.Frame.GoBack();
                }
            }
        }
    }
}
