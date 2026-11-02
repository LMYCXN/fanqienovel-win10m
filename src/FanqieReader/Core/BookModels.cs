using System;
using System.Collections.Generic;
using System.Globalization;

namespace FanqieReader.Core
{
    public sealed class Book
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Author { get; private set; }
        public string Summary { get; private set; }
        public string CoverUrl { get; private set; }
        public BookMetrics Metrics { get; private set; }
        public Book(string id, string title, string author, string summary, string coverUrl = null, BookMetrics metrics = null)
        {
            Id = id; Title = title; Author = author; Summary = summary; CoverUrl = coverUrl;
            Metrics = metrics ?? new BookMetrics();
        }
    }

    public sealed class BookMetrics
    {
        public long? Readers { get; set; }
        public double? Rating { get; set; }
        public long? Shelves { get; set; }
        public long? UpdatedAt { get; set; }
        public string Status { get; set; }
        public string Category { get; set; }
        public string DisplayText
        {
            get
            {
                var parts = new List<string>();
                if (Rating.HasValue) parts.Add(Rating.Value.ToString("0.0", CultureInfo.InvariantCulture) + " 分");
                if (Readers.HasValue && Readers.Value > 0)
                    parts.Add((Readers.Value >= 10000 ? (Readers.Value / 10000.0).ToString("0.#", CultureInfo.InvariantCulture) + "万" : Readers.Value.ToString(CultureInfo.InvariantCulture)) + "人在读");
                return string.Join(" · ", parts);
            }
        }
    }

    public sealed class Chapter
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public Chapter(string id, string title) { Id = id; Title = title; }
    }

    public sealed class LibraryEntry
    {
        public Book Book { get; private set; }
        public string ChapterId { get; private set; }
        public string ChapterTitle { get; private set; }
        public DateTimeOffset LastReadAt { get; private set; }
        public DateTimeOffset FavoritedAt { get; private set; }
        public bool IsFavorite { get; private set; }
        public string ProgressText { get { return string.IsNullOrEmpty(ChapterId) ? "尚未阅读" : "读至：" + (string.IsNullOrEmpty(ChapterTitle) ? "上次阅读的章节" : ChapterTitle); } }
        public string LastReadText { get { return LastReadAt == DateTimeOffset.MinValue ? "" : LastReadAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } }

        public LibraryEntry(Book book, string chapterId, string chapterTitle, DateTimeOffset lastReadAt, DateTimeOffset favoritedAt, bool isFavorite)
        {
            Book = book; ChapterId = chapterId; ChapterTitle = chapterTitle;
            LastReadAt = lastReadAt; FavoritedAt = favoritedAt; IsFavorite = isFavorite;
        }
    }

    public sealed class ChapterContent
    {
        public string ChapterId { get; private set; }
        public IReadOnlyList<string> Paragraphs { get; private set; }
        public bool IsComplete { get; private set; }
        public ChapterContent(string chapterId, IList<string> paragraphs, bool isComplete)
        {
            if (paragraphs == null) throw new ArgumentNullException(nameof(paragraphs));
            ChapterId = chapterId;
            Paragraphs = new List<string>(paragraphs).AsReadOnly();
            IsComplete = isComplete;
        }
    }
}
