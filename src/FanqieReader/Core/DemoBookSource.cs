using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FanqieReader.Core
{
    public sealed class DemoBookSource : IBookSource
    {
        private readonly List<Book> books = new List<Book>
        {
            new Book("demo-harbor", "灯塔来信", "演示作者", "原创演示故事。小城的旧灯塔收到一封迟来的信，修表师决定沿着海岸寻找寄信人。用于验证离线阅读，不是番茄在线书籍。"),
            new Book("demo-train", "末班列车", "演示作者", "原创演示故事。一位列车员在最后一班车上发现遗失的笔记本。用于验证搜索、目录切换和进度保存。")
        };
        private readonly Dictionary<string, List<Chapter>> catalogs = new Dictionary<string, List<Chapter>>();
        private readonly Dictionary<string, ChapterContent> contents = new Dictionary<string, ChapterContent>();

        public DemoBookSource()
        {
            AddBook("demo-harbor", new[] { "第一章 旧信", "第二章 退潮以后", "第三章 灯重新亮起" }, new[]
            {
                new[] { "镇上的钟停在六点一刻。林舟拆开钟壳，发现里面夹着一封信。纸已经发黄，收信人的名字却写得很清楚：灯塔管理员周远。", "他带着信走向海边。石阶上留着昨夜的雨水，卖鱼的摊主正把木箱搬回屋里。林舟问起周远，摊主停了一下，说灯塔已经关了十年。", "门没有锁。林舟推开门时，一枚铜钥匙从门框上掉下来，落在他的鞋边。楼上有人问：你终于来了吗？" },
                new[] { "楼上是一位拄着拐杖的老人。他没有接信，只让林舟先把窗打开。潮水退了，远处露出一条细窄的石路。", "信是我写的。老人说，寄出去的那一天，我以为自己再也不会回来。后来每次想去取，都怕那个人已经读过。", "林舟把信放到桌上，没有拆开。他望着海上的石路，问老人要不要一起走一趟。老人找了很久，才从抽屉里翻出另一把钥匙。" },
                new[] { "石路尽头的小屋还在。窗台上摆着一只空花盆，门后的刻痕记着几十年前的潮位。老人摸过那些刻痕，终于拆开了信。", "第二天，林舟修好了镇上的钟。六点一刻，钟声穿过街巷，灯塔也亮了一下。那不是航标，只是一盏放在窗口的台灯。", "老人坐在灯旁写回信。林舟没有问他寄给谁，只把一枚新邮票放在桌角。太阳落下去的时候，海面上还有一条亮着的路。" }
            });
            AddBook("demo-train", new[] { "第一章 遗失物", "第二章 终点站" }, new[]
            {
                new[] { "最后一班车驶出站台，车厢里只剩三个乘客。陈明检查座位时，发现一本蓝色笔记本，第一页画着一座没有名字的车站。", "笔记本里记着每一站的天气。有一页只写了一句话：如果下次还能回来，请替我看看那棵树。陈明翻到最后，发现终点站的站名被圈了起来。", "靠窗的女孩说，那是她爷爷的本子。她今天第一次坐这条线，不知道终点站还有没有那棵树。" },
                new[] { "到站时，站台只亮着一盏灯。陈明陪女孩走过旧候车室，在围墙后看见一棵很高的树。叶子几乎落光了，枝头还挂着一枚纸风车。", "女孩拍了一张照片，夹进笔记本。她说爷爷明年春天就能出院，到时候他们一起坐第一班车来。", "陈明收好钥匙，站在门口等她。末班车已经结束，明天的时刻表贴在玻璃上，每一站都还在。" }
            });
        }

        private void AddBook(string id, string[] titles, string[][] paragraphs)
        {
            var list = new List<Chapter>();
            for (var i = 0; i < titles.Length; i++)
            {
                var chapterId = id + "-" + (i + 1);
                list.Add(new Chapter(chapterId, titles[i]));
                contents.Add(chapterId, new ChapterContent(chapterId, paragraphs[i], true));
            }
            catalogs.Add(id, list);
        }

        public Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = (keyword ?? "").Trim();
            IReadOnlyList<Book> result = books.Where(b => b.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || b.Author.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList().AsReadOnly();
            return Task.FromResult(result);
        }

        public Task<Book> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var book = books.FirstOrDefault(b => b.Id == bookId);
            if (book == null) throw new KeyNotFoundException("找不到这本演示书籍。");
            return Task.FromResult(book);
        }

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(string bookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Chapter> chapters;
            if (!catalogs.TryGetValue(bookId, out chapters)) throw new KeyNotFoundException("找不到这本书的目录。");
            return Task.FromResult<IReadOnlyList<Chapter>>(chapters.AsReadOnly());
        }

        public Task<ChapterContent> GetChapterAsync(string chapterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChapterContent content;
            if (!contents.TryGetValue(chapterId, out content)) throw new KeyNotFoundException("找不到这个章节。");
            return Task.FromResult(content);
        }
    }
}
