using System;
using System.Collections.Generic;
using System.Linq;

namespace FanqieReader.Core
{
    internal static class RecommendationSelector
    {
        internal static double Quality(Book book, DateTimeOffset now)
        {
            var metrics = book.Metrics;
            // Missing signals contribute no weight. This is a local selection score, not a platform rating.
            double total = 0, weight = 0;
            if (metrics.Rating.HasValue) { total += 0.55 * metrics.Rating.Value / 10; weight += 0.55; }
            if (metrics.Readers.HasValue) { total += 0.30 * Math.Min(1, Math.Log10(1 + (double)metrics.Readers.Value) / 7); weight += 0.30; }
            if (metrics.Shelves.HasValue) { total += 0.10 * Math.Min(1, Math.Log10(1 + (double)metrics.Shelves.Value) / 6); weight += 0.10; }
            if (metrics.Status == "0") { total += 0.05; weight += 0.05; }
            else if (metrics.Status == "1" && metrics.UpdatedAt.HasValue)
            {
                var ageDays = Math.Max(0, (now.ToUnixTimeSeconds() - metrics.UpdatedAt.Value) / 86400.0);
                total += 0.05 * Math.Exp(-ageDays / 30); weight += 0.05;
            }
            return weight == 0 ? 0.5 : total / weight;
        }

        internal static IReadOnlyList<Book> Select(IEnumerable<Book> candidates, IList<string> recentIds, int count, int perCategory, Random random)
        {
            var now = DateTimeOffset.UtcNow;
            var ranked = candidates.GroupBy(book => book.Id).Select(group => group.First())
                .Where(book => book.Metrics.Status != "4" && (!book.Metrics.Rating.HasValue || book.Metrics.Rating.Value >= 7))
                .Select(book => new
                {
                    Book = book,
                    Seen = recentIds.IndexOf(book.Id),
                    // Weighted sampling varies books within the quality pool instead of merely shuffling a fixed page.
                    Draw = -Math.Log(1 - random.NextDouble()) / (0.05 + Math.Pow(Quality(book, now), 4))
                })
                .OrderBy(item => item.Seen).ThenBy(item => item.Draw).ToList();
            var selected = new List<Book>();
            var categories = new Dictionary<string, int>();
            foreach (var item in ranked)
            {
                var category = item.Book.Metrics.Category ?? "";
                int used;
                categories.TryGetValue(category, out used);
                if (category.Length > 0 && used >= perCategory) continue;
                selected.Add(item.Book);
                categories[category] = used + 1;
                if (selected.Count == count) break;
            }
            if (selected.Count < count)
            {
                var ids = new HashSet<string>(selected.Select(book => book.Id));
                foreach (var item in ranked)
                {
                    if (ids.Add(item.Book.Id)) selected.Add(item.Book);
                    if (selected.Count == count) break;
                }
            }
            return selected.OrderByDescending(book => Quality(book, now)).ToList().AsReadOnly();
        }
    }
}
