using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FanqieReader.Core
{
    public interface IBookSource
    {
        Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken);
        Task<Book> GetBookAsync(string bookId, CancellationToken cancellationToken);
        Task<IReadOnlyList<Chapter>> GetChaptersAsync(string bookId, CancellationToken cancellationToken);
        Task<ChapterContent> GetChapterAsync(string chapterId, CancellationToken cancellationToken);
    }
}
