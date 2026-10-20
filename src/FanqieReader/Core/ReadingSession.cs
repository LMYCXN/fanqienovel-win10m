using System;
using System.Collections.Generic;

namespace FanqieReader.Core
{
    public sealed class ReadingSession
    {
        private readonly IReadOnlyList<Chapter> chapters;
        public string BookId { get; private set; }
        public int Index { get; private set; }
        public Chapter Current { get { return chapters[Index]; } }
        public int Count { get { return chapters.Count; } }
        public bool CanPrevious { get { return Index > 0; } }
        public bool CanNext { get { return Index + 1 < chapters.Count; } }

        public ReadingSession(string bookId, IReadOnlyList<Chapter> chapters, string chapterId)
        {
            if (chapters == null || chapters.Count == 0) throw new ArgumentException("目录不能为空。", nameof(chapters));
            BookId = bookId;
            this.chapters = new List<Chapter>(chapters).AsReadOnly();
            Index = 0;
            for (var i = 0; i < chapters.Count; i++)
                if (chapters[i].Id == chapterId) { Index = i; break; }
        }

        public bool Move(int delta)
        {
            var target = (long)Index + delta;
            if (target < 0 || target >= chapters.Count) return false;
            Index = (int)target;
            return true;
        }
    }
}
