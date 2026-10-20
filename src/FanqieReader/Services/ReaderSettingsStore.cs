using System;
using Windows.Storage;

namespace FanqieReader.Services
{
    public sealed class ReaderSettingsStore
    {
        private readonly ApplicationDataContainer local = ApplicationData.Current.LocalSettings;
        public double FontSize { get; private set; }
        public bool IsNight { get; private set; }
        public string BookId { get; private set; }
        public string ChapterId { get; private set; }

        public ReaderSettingsStore()
        {
            var size = local.Values["fontSize"];
            FontSize = size is double ? Math.Max(14, Math.Min(36, (double)size)) : 22;
            IsNight = local.Values["night"] is bool && (bool)local.Values["night"];
            var progress = local.Values["progress"] as ApplicationDataCompositeValue;
            // Only restore online progress; older versions also stored offline examples.
            if (progress != null && progress["source"] as string == "online")
            {
                BookId = progress["bookId"] as string;
                ChapterId = progress["chapterId"] as string;
            }
        }

        public void SaveAppearance(double fontSize, bool night)
        {
            FontSize = Math.Max(14, Math.Min(36, fontSize));
            IsNight = night;
            local.Values["fontSize"] = FontSize;
            local.Values["night"] = IsNight;
        }

        public void SaveProgress(string bookId, string chapterId)
        {
            local.Values["progress"] = new ApplicationDataCompositeValue
            {
                ["bookId"] = bookId,
                ["chapterId"] = chapterId,
                ["source"] = "online"
            };
            BookId = bookId;
            ChapterId = chapterId;
        }
    }
}
