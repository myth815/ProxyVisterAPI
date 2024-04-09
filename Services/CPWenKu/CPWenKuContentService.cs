using HtmlAgilityPack;
using Newtonsoft.Json;
using ProxyVisterAPI.Models.CPWenku;
using System.Collections.Concurrent;
using System.Globalization;

namespace ProxyVisterAPI.Services.CPWenKu
{
    public interface ICPWenKuCrawerService
    {
        bool TryGetBookOrAddBookToCrawerQueue(BookModel book, bool force = false);
        List<CategoryModel> GetCategories(bool force = false);
        List<BookModel>? GetBookListFromCategory(string category, bool force = false);
        List<BookModel> GetBookListFromAuthor(string Author);
        BookModel? GetBookModel(int BookID);
    }

    public class CPWenKuContentService : ICPWenKuCrawerService
    {
        private Logger<JsonLocalStorageService> Logger;
        private ICrawerService CrawerService;

        private readonly string StorageDirectory = Directory.GetCurrentDirectory();

        private readonly string CategoriesLocalPath = "/cpwenku/Categories.json";
        private ConcurrentQueue<BookModel> BookCrawerList = new ConcurrentQueue<BookModel>();

        public CPWenKuContentService(Logger<JsonLocalStorageService> ServiceLogger, ICrawerService CrawerService)
        {
            this.Logger = ServiceLogger;
            this.CrawerService = CrawerService;
        }

        private BookModel? ReadBookModelFromLocal(int BookID)
        {
            string LocalBookPath = this.StorageDirectory + $"/cpwenku/Books/{BookID}.json";
            if (System.IO.File.Exists(LocalBookPath))
            {
                return JsonConvert.DeserializeObject<BookModel>(System.IO.File.ReadAllText(LocalBookPath));
            }
            return null;
        }

        private List<CategoryModel>? ReadCategoriesFromLocal()
        {
            string CategoriesLocalFullPath = this.StorageDirectory + this.CategoriesLocalPath;
            if (System.IO.File.Exists(CategoriesLocalFullPath))
            {
                return JsonConvert.DeserializeObject<List<CategoryModel>>(System.IO.File.ReadAllText(CategoriesLocalFullPath));
            }
            return null;
        }

        private List<BookModel>? ReadBooksFromCategoryFromLocal()
        {
            return null;
        }

        public List<CategoryModel> GetCategories(bool force = false)
        {
            List<CategoryModel>? Result = ReadCategoriesFromLocal();
            if (Result == null)
            {
                HtmlDocument WebContent = this.CrawerService.GetWebContent("https://www.cpwenku.com/all.html").Result;
                Result = new List<CategoryModel>();
                HtmlNodeCollection HtmlNodes = WebContent.DocumentNode.SelectNodes("//div[@class='class']//li//a");
                if (HtmlNodes != null)
                {
                    foreach (HtmlNode node in HtmlNodes)
                    {
                        var Category = new CategoryModel
                        {
                            Name = node.InnerText,
                            Link = node.GetAttributeValue("href", string.Empty)
                        };
                        Result.Add(Category);
                    }
                }
                //将结果写入文件
                System.IO.File.WriteAllText(this.StorageDirectory + this.CategoriesLocalPath, JsonConvert.SerializeObject(Result));
            }
            return Result;
        }

        public List<BookModel>? GetBookListFromCategory(string category, bool force = false)
        {
            List<BookModel>? Result = this.ReadBooksFromCategoryFromLocal();
            if (Result == null)
            {
                HtmlDocument WebContent = this.CrawerService.GetWebContent("https://www.cpwenku.com/all.html").Result;
                Result = new List<BookModel>();
            }
            return Result;
        }

        public List<BookModel> GetBookListFromAuthor(string Author)
        {
            var RequestURL = $"https://www.cpwenku.net/modules/article/authorarticle.php?author={Author}";
            List<BookModel> Result = new List<BookModel>();
            HtmlDocument WebContent = this.CrawerService.GetWebContent(RequestURL).Result;
            //读取列表
            return Result;
        }

        public BookModel? GetBookModel(int BookID)
        {
            BookModel? LocalBookModel = this.ReadBookModelFromLocal(BookID);

            string RequestURL = $"https://www.cpwenku.com/go/{BookID}/";
            HtmlDocument WebContent = this.CrawerService.GetWebContent(RequestURL).Result;
            if (LocalBookModel != null)
            {
                DateTime RemoteUpdateTime = DateTime.ParseExact(WebContent.DocumentNode.SelectSingleNode("//p[@class='booktime']").InnerText.Replace("更新时间：", ""), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                if (RemoteUpdateTime == LocalBookModel.UpdateTime)
                {
                    if (LocalBookModel.WordsCount == 0)
                    {
                        TryGetBookOrAddBookToCrawerQueue(LocalBookModel, true);
                    }
                    return LocalBookModel;
                }
            }

            //强制更新
            HtmlNode BookInfo = WebContent.DocumentNode.SelectSingleNode("//div[@class='bookinfo']");
            HtmlNode BoolTag = BookInfo.ChildNodes[1];
            HtmlNode ListChapterAll = WebContent.DocumentNode.SelectSingleNode("//div[@id='list-chapterAll']");
            int ChapterNumber = 0;
            List<string> ChapterPageList = new List<string>();
            for (int i = 0; i < ListChapterAll.ChildNodes.Count; i++)
            {
                HtmlNode childNode = ListChapterAll.ChildNodes[i];
                if (childNode.Name == "dd")
                {
                    ChapterNumber++;
                    string PageLink = childNode.ChildNodes[0].Attributes["href"].Value;
                    string PageTitle = childNode.InnerText;
                    if (uint.Parse(PageTitle.Replace("第", "").Replace("节", "")) == ChapterNumber)
                    {
                        ChapterPageList.Add(PageLink);
                    }
                }
            }

            BookModel ResultBookModel = new BookModel
            {
                ID = BookID,
                CoverPath = WebContent.DocumentNode.SelectSingleNode("//img[@class='thumbnail']").Attributes["src"].Value,
                NoCoverPath = WebContent.DocumentNode.SelectSingleNode("//img[@class='thumbnail']").Attributes["onerror"].Value,
                Title = BookInfo.SelectSingleNode("//h1[@class='booktitle']").InnerText,
                Author = BoolTag.SelectSingleNode("//a[@class='red']").InnerText,
                Finished = BoolTag.SelectSingleNode("//span[@class='red']").InnerText == "已完结",
                Introduction = BookInfo.SelectSingleNode("//p[@class='bookintro']").InnerText,
                UpdateTime = DateTime.ParseExact(WebContent.DocumentNode.SelectSingleNode("//p[@class='booktime']").InnerText.Replace("更新时间：", ""), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ChapterPageList = ChapterPageList,
                Link = RequestURL,
            };
            //将BookModel存入本地
            string SerializedBookModel = JsonConvert.SerializeObject(ResultBookModel);
            string LocalBookPath = this.StorageDirectory + $"/cpwenku/Books/{BookID}.json";
            System.IO.File.WriteAllText(LocalBookPath, SerializedBookModel);
            TryGetBookOrAddBookToCrawerQueue(ResultBookModel, true);
            return ResultBookModel;
        }

        public bool TryGetBookOrAddBookToCrawerQueue(BookModel book, bool force = false)
        {
            if (CurrentTask != null && CurrentTaskBookID == book.ID)
            {
                return false;
            }
            bool FoundSameBook = false;
            foreach (BookModel BookModelInstance in this.BookCrawerList)
            {
                if (BookModelInstance.ID == book.ID)
                {
                    FoundSameBook = true;
                    break;
                }
            }
            if (!FoundSameBook)
            {
                this.BookCrawerList.Enqueue(book);
                this.FlushBookToCrawerList();
                return true;
            }
            else
            {
                return false;
            }
        }

        private async Task ProcessBookModel(BookModel BookModelInstance)
        {
            // 实现你的异步逻辑
            await Task.Delay(TimeSpan.FromSeconds(1)); // 假设的异步操作
            List<string> Result = new List<string>();

            for (int i = 0; i < BookModelInstance.ChapterPageList.Count; i++)
            {
                string RequestURL = $"https://www.cpwenku.com/go/{BookModelInstance.ID}/{BookModelInstance.ChapterPageList[i]}";
                HtmlDocument WebContent = this.CrawerService.GetWebContent(RequestURL).Result;
                HtmlNode ContentNode = WebContent.DocumentNode.SelectSingleNode("//div[@class='readcontent']");
                if (ContentNode != null)
                {
                    ContentNode.RemoveChild(ContentNode.ChildNodes[0]);
                    ContentNode.RemoveChild(ContentNode.ChildNodes[0]);
                    ContentNode.RemoveChild(ContentNode.ChildNodes[ContentNode.ChildNodes.Count - 1]);
                    ContentNode.RemoveChild(ContentNode.ChildNodes[ContentNode.ChildNodes.Count - 1]);
                    Result.Add(ContentNode.InnerText);
                    this.Logger.LogInformation($"{BookModelInstance.ID} Processed {i + 1} / {BookModelInstance.ChapterPageList.Count}");
                }
            }
            BookModelInstance.ChapterPageContentList = Result;
            string SerializedBookModel = JsonConvert.SerializeObject(BookModelInstance);
            string LocalBookPath = this.StorageDirectory + $"/cpwenku/Books/{BookModelInstance.ID}.json";
            System.IO.File.WriteAllText(LocalBookPath, SerializedBookModel);
            this.Logger.LogInformation($"Finish Processed {BookModelInstance.ID}");
        }

        private void FlushBookToCrawerList()
        {
            if (!BookCrawerList.IsEmpty)
            {
                BookModel? firstNode;
                if (BookCrawerList.TryDequeue(out firstNode) && firstNode != null)
                {
                    this.CurrentTaskBookID = firstNode.ID;
                    // 在这里处理 firstNode 对象
                    this.CurrentTask = Task.Run(() => ProcessBookDetail(firstNode)).ContinueWith(task =>
                    {
                        if (task.Status == TaskStatus.RanToCompletion)
                        {
                            this.CurrentTask = null;
                            this.CurrentTaskBookID = 0;
                            FlushBookToCrawerList();
                        }
                    })
                    ;
                }
            }
        }
    }
}