using System;
using System.Collections.Generic;

namespace FanqieReader.Core
{
    public sealed class Book
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Author { get; private set; }
        public string Summary { get; private set; }
        public Book(string id, string title, string author, string summary)
        {
            Id = id; Title = title; Author = author; Summary = summary;
        }
    }

    public sealed class Chapter
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public bool IsFreeConfirmed { get; private set; }
        public Chapter(string id, string title, bool isFreeConfirmed = false) { Id = id; Title = title; IsFreeConfirmed = isFreeConfirmed; }
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
