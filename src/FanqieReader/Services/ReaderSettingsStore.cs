using System;
using System.Collections.Generic;
using System.Linq;
using FanqieReader.Core;
using Windows.Storage;

namespace FanqieReader.Services
{
    public sealed class ReaderSettingsStore
    {
        private readonly ApplicationDataContainer local = ApplicationData.Current.LocalSettings;
        private readonly ApplicationDataContainer library;
        public double FontSize { get; private set; }
        public bool IsNight { get; private set; }
        public string BookId { get; private set; }
        public string ChapterId { get; private set; }

        public ReaderSettingsStore()
        {
            library = local.CreateContainer("library", ApplicationDataCreateDisposition.Always);
            var size = local.Values["fontSize"];
            FontSize = size is double ? Math.Max(14, Math.Min(36, (double)size)) : 22;
            IsNight = local.Values["night"] is bool && (bool)local.Values["night"];
            var progress = local.Values["progress"] as ApplicationDataCompositeValue;
            // Only restore online progress; older versions also stored offline examples.
            if (progress != null && progress["source"] as string == "online")
            {
                BookId = progress["bookId"] as string;
                ChapterId = progress["chapterId"] as string;
                if (!string.IsNullOrEmpty(BookId) && !string.IsNullOrEmpty(ChapterId) && !library.Values.ContainsKey(BookId))
                {
                    library.Values[BookId] = new ApplicationDataCompositeValue
                    {
                        ["chapterId"] = ChapterId,
                        ["lastReadAt"] = DateTimeOffset.UtcNow.UtcDateTime.Ticks
                    };
                }
            }
        }

        public void SaveAppearance(double fontSize, bool night)
        {
            FontSize = Math.Max(14, Math.Min(36, fontSize));
            IsNight = night;
            local.Values["fontSize"] = FontSize;
            local.Values["night"] = IsNight;
        }

        public IReadOnlyList<LibraryEntry> GetHistory()
        {
            return ReadEntries().Where(entry => !string.IsNullOrEmpty(entry.ChapterId)).OrderByDescending(entry => entry.LastReadAt).ToList().AsReadOnly();
        }

        public IReadOnlyList<LibraryEntry> GetFavorites()
        {
            return ReadEntries().Where(entry => entry.IsFavorite).OrderByDescending(entry => entry.FavoritedAt).ToList().AsReadOnly();
        }

        public LibraryEntry GetProgress(string bookId)
        {
            var value = library.Values[bookId] as ApplicationDataCompositeValue;
            return value == null ? null : ReadEntry(bookId, value);
        }

        public bool IsFavorite(string bookId)
        {
            var entry = GetProgress(bookId);
            return entry != null && entry.IsFavorite;
        }

        public void UpdateBook(Book book)
        {
            var value = library.Values[book.Id] as ApplicationDataCompositeValue;
            if (value == null) return;
            SetMetadata(value, book);
            library.Values[book.Id] = value;
        }

        public void SetFavorite(Book book, bool favorite)
        {
            var value = library.Values[book.Id] as ApplicationDataCompositeValue ?? new ApplicationDataCompositeValue();
            SetMetadata(value, book);
            value["favorite"] = favorite;
            if (favorite) value["favoritedAt"] = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
            library.Values[book.Id] = value;
        }

        public void SaveProgress(Book book, Chapter chapter)
        {
            var value = library.Values[book.Id] as ApplicationDataCompositeValue ?? new ApplicationDataCompositeValue();
            SetMetadata(value, book);
            value["chapterId"] = chapter.Id;
            value["chapterTitle"] = chapter.Title;
            value["lastReadAt"] = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
            library.Values[book.Id] = value;
            local.Values["progress"] = new ApplicationDataCompositeValue
            {
                ["bookId"] = book.Id,
                ["chapterId"] = chapter.Id,
                ["source"] = "online"
            };
            BookId = book.Id;
            ChapterId = chapter.Id;
        }

        private IEnumerable<LibraryEntry> ReadEntries()
        {
            foreach (var pair in library.Values)
            {
                var value = pair.Value as ApplicationDataCompositeValue;
                if (value != null) yield return ReadEntry(pair.Key, value);
            }
        }

        private static LibraryEntry ReadEntry(string bookId, ApplicationDataCompositeValue value)
        {
            var book = new Book(bookId, value["title"] as string ?? "上次阅读的书籍", value["author"] as string ?? "", "", value["coverUrl"] as string);
            return new LibraryEntry(book, value["chapterId"] as string, value["chapterTitle"] as string,
                ReadTime(value["lastReadAt"]), ReadTime(value["favoritedAt"]), value["favorite"] is bool && (bool)value["favorite"]);
        }

        private static DateTimeOffset ReadTime(object value)
        {
            return value is long ? new DateTimeOffset((long)value, TimeSpan.Zero) : DateTimeOffset.MinValue;
        }

        private static void SetMetadata(ApplicationDataCompositeValue value, Book book)
        {
            value["title"] = book.Title;
            value["author"] = book.Author;
            value["coverUrl"] = book.CoverUrl ?? "";
        }
    }
}
