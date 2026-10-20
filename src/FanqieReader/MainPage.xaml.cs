using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using FanqieReader.Core;
using FanqieReader.Services;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using system.Linq;

namespace FanqieReader
{
    public sealed partial class MainPage : Page
    {
        private IBookSource source = new DemoBookSource();
        private readonly ObservableCollection<Book> visibleBooks = new ObservableCollection<Book>();
        private bool IsOnline { get { return source is OnlineBookSource; } }
        private bool CanResume { get { return !string.IsNullOrEmpty(settings.BookId) && (IsOnline ? "online" : "demo") == settings.Source; } }
        private CancellationTokenSource readerCancellation;
        private readonly ReaderSettingsStore settings = new ReaderSettingsStore();
        private Book currentBook;
        private IReadOnlyList<Chapter> currentCatalog;
        private ReadingSession session;
        private bool initialized;
        private bool readerBusy;
        private bool summaryExpanded;
        private string readerView = "books";

        public MainPage()
        {
            InitializeComponent();
            BooksList.ItemsSource = visibleBooks;
            NightSwitch.IsOn = settings.IsNight;
            if (settings.Source == "online")
            {
                source = new OnlineBookSource();
                SourcePicker.SelectedIndex = 1;
                SearchBox.PlaceholderText = "输入番茄书名或作者";
            }
            ResumeButton.IsEnabled = CanResume;
            initialized = true;
            Loaded += MainPage_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);
            SystemNavigationManager.GetForCurrentView().BackRequested += BackRequested;
            UpdateBackButton();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs args)
        {
            SystemNavigationManager.GetForCurrentView().BackRequested -= BackRequested;
            CancelPendingLoad();
            base.OnNavigatedFrom(args);
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs args)
        {
            Loaded -= MainPage_Loaded;
            await ReaderActionAsync(SearchAsync);
        }

        public void CancelPendingLoad() { readerCancellation?.Cancel(); }

        private async Task ReaderActionAsync(Func<Task> action)
        {
            if (readerBusy) return;
            readerBusy = true;
            readerCancellation = new CancellationTokenSource();
            SourcePicker.IsEnabled = false;
            SearchBox.IsEnabled = false;
            LoadMoreButton.IsEnabled = false;
            ReaderProgress.Visibility = Visibility.Visible;
            CancelReaderButton.Visibility = Visibility.Visible;
            ReaderStatusText.Text = "正在加载…";
            try { await action(); }
            catch (OperationCanceledException) { ReaderStatusText.Text = "加载已取消。"; }
            catch (Exception error) { ReaderStatusText.Text = "操作失败：" + error.Message; }
            finally
            {
                readerCancellation.Dispose(); readerCancellation = null;
                readerBusy = false; SourcePicker.IsEnabled = true; SearchBox.IsEnabled = true; LoadMoreButton.IsEnabled = true;
                ReaderProgress.Visibility = Visibility.Collapsed; CancelReaderButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelReader_Click(object sender, RoutedEventArgs args) { readerCancellation?.Cancel(); }

        private async Task SearchAsync()
        {
            var books = await source.SearchAsync(SearchBox.Text, readerCancellation.Token);
            visibleBooks.Clear();
            foreach (var book in books) visibleBooks.Add(book);
            UpdateSearchStatus();
            ShowReaderView("books");
        }

        private void UpdateSearchStatus()
        {
            var online = source as OnlineBookSource;
            LoadMoreButton.Visibility = online != null && online.HasMore ? Visibility.Visible : Visibility.Collapsed;
            ReaderStatusText.Text = IsOnline
                ? (string.IsNullOrWhiteSpace(SearchBox.Text) ? "输入书名开始在线搜索。" : "番茄在线 · " + visibleBooks.Count + " 本书")
                : (visibleBooks.Count == 0 ? "没有匹配的演示书籍。" : "离线演示数据 · " + visibleBooks.Count + " 本书");
        }

        private async void SourcePicker_SelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (!initialized || readerBusy) return;
            source = SourcePicker.SelectedIndex == 1 ? (IBookSource)new OnlineBookSource() : new DemoBookSource();
            SearchBox.Text = "";
            SearchBox.PlaceholderText = IsOnline ? "输入番茄书名或作者" : "搜索演示书名或作者";
            ResumeButton.IsEnabled = CanResume;
            ChaptersList.IsItemClickEnabled = true;
            session = null; currentBook = null; currentCatalog = null;
            visibleBooks.Clear(); LoadMoreButton.Visibility = Visibility.Collapsed;
            await ReaderActionAsync(SearchAsync);
        }

        private async void LoadMore_Click(object sender, RoutedEventArgs args)
        {
            await ReaderActionAsync(async () =>
            {
                var online = source as OnlineBookSource;
                if (online == null) return;
                var seen = new HashSet<string>();
                foreach (var book in visibleBooks) seen.Add(book.Id);
                foreach (var book in await online.LoadMoreAsync(readerCancellation.Token))
                    if (seen.Add(book.Id)) visibleBooks.Add(book);
                UpdateSearchStatus();
            });
        }

        private async void Search_Click(object sender, RoutedEventArgs args) { await ReaderActionAsync(SearchAsync); }
        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key != VirtualKey.Enter) return;
            args.Handled = true;
            await ReaderActionAsync(SearchAsync);
        }

        private async Task OpenBookAsync(string bookId)
        {
            var book = await source.GetBookAsync(bookId, readerCancellation.Token);
            var catalog = await source.GetChaptersAsync(bookId, readerCancellation.Token);
            currentBook = book;
            currentCatalog = catalog;
            BookTitleText.Text = currentBook.Title;
            BookSummaryText.Text = currentBook.Summary;
            summaryExpanded = false;
            UpdateSummary();
            ChaptersList.ItemsSource = currentCatalog;
            ReaderStatusText.Text = (IsOnline ? "番茄在线 · " : "离线演示数据 · ") + currentCatalog.Count + " 章";
            ShowReaderView("catalog");
        }

        private async void Book_Click(object sender, ItemClickEventArgs args)
        {
            var book = (Book)args.ClickedItem;
            await ReaderActionAsync(() => OpenBookAsync(book.Id));
        }

        private async void Chapter_Click(object sender, ItemClickEventArgs args)
        {
            var chapter = (Chapter)args.ClickedItem;
            await ReaderActionAsync(async () =>
            {
                session = new ReadingSession(currentBook.Id, currentCatalog, chapter.Id);
                await RenderChapterAsync();
            });
        }

        private async void Resume_Click(object sender, RoutedEventArgs args)
        {
            await ReaderActionAsync(async () =>
            {
                var chapterId = settings.ChapterId;
                await OpenBookAsync(settings.BookId);
                session = new ReadingSession(currentBook.Id, currentCatalog, chapterId);
                await RenderChapterAsync();
            });
        }

        private async Task RenderChapterAsync()
        {
            var chapter = session.Current;
            var content = await source.GetChapterAsync(chapter.Id, readerCancellation.Token);
            ChapterTitleText.Text = chapter.Title;
            ParagraphsPanel.Children.Clear();
            foreach (var paragraph in content.Paragraphs)
            {
                ParagraphsPanel.Children.Add(new TextBlock
                {
                    Text = "　　" + paragraph,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Margin = new Thickness(0, 0, 0, 18)
                });
            }
            PreviousButton.IsEnabled = session.CanPrevious;
            NextButton.IsEnabled = session.CanNext;
            ReaderStatusText.Text = (IsOnline ? "番茄在线 · " : "演示数据 · ") + (content.IsComplete ? "完整章节" : "片段或完整性未确认") + " · " + content.Paragraphs.Count + " 段 · " + (session.Index + 1) + "/" + session.Count;
            ApplyAppearance();
            ShowReaderView("reading");
            ChapterScroller.ChangeView(null, 0, null, true);
            settings.SaveProgress(session.BookId, chapter.Id, IsOnline ? "online" : "demo");
            ResumeButton.IsEnabled = true;
        }

        private async Task MoveChapterAsync(int delta)
        {
            if (session != null && session.Move(delta))
            {
                try { await RenderChapterAsync(); }
                catch { session.Move(-delta); throw; }
            }
        }

        private async void Previous_Click(object sender, RoutedEventArgs args) { await ReaderActionAsync(() => MoveChapterAsync(-1)); }
        private async void Next_Click(object sender, RoutedEventArgs args) { await ReaderActionAsync(() => MoveChapterAsync(1)); }
        private void BackToBooks_Click(object sender, RoutedEventArgs args) { ShowReaderView("books"); }
        private void BackToCatalog_Click(object sender, RoutedEventArgs args) { ShowReaderView("catalog"); }

        private void ToggleSummary_Click(object sender, RoutedEventArgs args)
        {
            summaryExpanded = !summaryExpanded;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            BookSummaryText.MaxLines = summaryExpanded ? 0 : 4;
            BookSummaryText.TextTrimming = summaryExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            SummaryToggleButton.Content = summaryExpanded ? "收起简介" : "展开简介";
            SummaryToggleButton.Visibility = string.IsNullOrWhiteSpace(BookSummaryText.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SmallerFont_Click(object sender, RoutedEventArgs args) { SaveAppearance(settings.FontSize - 2); }
        private void LargerFont_Click(object sender, RoutedEventArgs args) { SaveAppearance(settings.FontSize + 2); }
        private void NightSwitch_Toggled(object sender, RoutedEventArgs args)
        {
            if (initialized) SaveAppearance(settings.FontSize);
        }

        private void SaveAppearance(double fontSize)
        {
            try
            {
                settings.SaveAppearance(fontSize, NightSwitch.IsOn);
                ApplyAppearance();
            }
            catch (Exception error) { ReaderStatusText.Text = "设置保存失败：" + error.Message; }
        }

        private void ApplyAppearance()
        {
            ReadingPanel.RequestedTheme = settings.IsNight ? ElementTheme.Dark : ElementTheme.Light;
            ReadingPanel.Background = new SolidColorBrush(settings.IsNight ? Color.FromArgb(255, 27, 29, 31) : Color.FromArgb(255, 250, 247, 239));
            var foreground = new SolidColorBrush(settings.IsNight ? Color.FromArgb(255, 224, 224, 218) : Color.FromArgb(255, 40, 40, 36));
            ChapterTitleText.Foreground = foreground;
            foreach (var item in ParagraphsPanel.Children)
            {
                var block = item as TextBlock;
                if (block == null) continue;
                block.FontSize = settings.FontSize;
                block.Foreground = foreground;
                block.LineHeight = settings.FontSize * 1.7;
            }
        }

        private void ShowReaderView(string view)
        {
            readerView = view;
            BooksPanel.Visibility = view == "books" ? Visibility.Visible : Visibility.Collapsed;
            CatalogPanel.Visibility = view == "catalog" ? Visibility.Visible : Visibility.Collapsed;
            ReadingPanel.Visibility = view == "reading" ? Visibility.Visible : Visibility.Collapsed;
            UpdateBackButton();
        }

        private void UpdateBackButton()
        {
            if (!initialized) return;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = readerView != "books"
                ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;
        }

        private void BackRequested(object sender, BackRequestedEventArgs args)
        {
            if (args.Handled) return;
            if (readerBusy) { args.Handled = true; readerCancellation?.Cancel(); return; }
            if (readerView != "books")
            {
                args.Handled = true;
                ShowReaderView(readerView == "reading" ? "catalog" : "books");
            }
        }
    }
}
