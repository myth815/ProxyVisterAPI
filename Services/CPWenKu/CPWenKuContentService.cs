using HtmlAgilityPack;
using Newtonsoft.Json;
using ProxyVisterAPI.Models.CPWenku;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace ProxyVisterAPI.Services.CPWenKu
{
    public class CPWenKuRequestDefine
    {
        public const string CategoriesURL = "https://www.cpwenku.net/all.html";
        public const string GetBooksWithauthorURL = "https://www.cpwenku.net/modules/article/authorarticle.php?author=";
        public const string GetBookWithIDURL = "https://www.cpwenku.net/go/";
        public const string GetPageWithChapterURL = "https://www.cpwenku.net/go/";
    }

    public interface ICPWenKuModelService
    {
        List<CategoryModel>? GetCategories(bool Force = false);
        List<BookModel>? GetBookListFromCategory(string Category, bool Force = false);
        List<BookModel>? GetBookListFromAuthor(string Author);
        BookModel? GetBookModel(int BookID);
        BookContentModel? GetBookContentModel(int BookID);
        bool IsBookModelEqual(BookModel Left, BookModel Right);
        bool IsBookModelUpdateTimeEqual(BookModel Left, BookModel Right, bool CheckUpdateTime);
        bool IsBookModelCollectionEqual(List<BookModel> Left, List<BookModel> Right);
        bool IsCategoryModelEqual(CategoryModel Left, CategoryModel Right);
    }

    public class CPWenKuModelService : ICPWenKuModelService
    {
        private ILogger<CPWenKuModelService> Logger;
        private ICrawerService CrawerService;
        private ICPWenKuModelParseService ModelParseService;
        private ICPWenKuLocalStrorageService LocalStrorageService;

        private readonly string StorageDirectory = Directory.GetCurrentDirectory();
        private ConcurrentQueue<BookModel> BookCrawerList = new ConcurrentQueue<BookModel>();

        private int CurrentTaskBookID;
        private Task? CurrentTask;

        public CPWenKuModelService(ILogger<CPWenKuModelService> ServiceLogger, ICrawerService CrawerService, ICPWenKuModelParseService ModelParseService, ICPWenKuLocalStrorageService localStrorageService)
        {
            this.Logger = ServiceLogger;
            this.CrawerService = CrawerService;
            this.ModelParseService = ModelParseService;
            this.LocalStrorageService = localStrorageService;
        }

        public List<CategoryModel>? GetCategories(bool Force = false)
        {
            List<CategoryModel>? LocalResult = this.LocalStrorageService.LoadCategoryModelFromLocalStrorage();
            if(LocalResult == null || Force)
            {
                List<CategoryModel>? RemoteResult = this.CrawerService.FetchWebContent<List<CategoryModel>>(CPWenKuRequestDefine.CategoriesURL);
                if (RemoteResult != null && (LocalResult == null || (LocalResult != null && !this.IsCategoryModelCollectionEqual(LocalResult, RemoteResult))))
                {
                    this.LocalStrorageService.SaveCategoryModelToLocalStrorage(RemoteResult);
                }
                return RemoteResult;
            }
            return LocalResult;
        }

        public List<BookModel>? GetBookListFromCategory(string Category, bool Force = false)
        {
            List<BookModel>? LocalResult = this.LocalStrorageService.LoadBookListWithCategoryFromLocal(Category);
            if (LocalResult == null || Force)
            {
                List<BookModel>? RemoteResult = this.CrawerService.FetchWebContent<List<BookModel>>(CPWenKuRequestDefine.CategoriesURL);
                if (RemoteResult != null &&  (LocalResult == null || (LocalResult != null && !this.IsBookModelCollectionEqual(LocalResult, RemoteResult))))
                {
                    this.LocalStrorageService.SaveBookListWithCategoryToLocal(RemoteResult, Category);
                }
                return RemoteResult;
            }
            return LocalResult;
        }

        public List<BookModel>? GetBookListFromAuthor(string Author)
        {
            var RequestURL = $"{CPWenKuRequestDefine.GetBooksWithauthorURL}{Author}";
            List<BookModel>? RemoteResult = this.CrawerService.FetchWebContent<List<BookModel>>(RequestURL);
            return RemoteResult;
        }

        public BookModel? GetBookModel(int BookID)
        {
            BookModel? LocalResult = this.LocalStrorageService.LoadBookModelFromLocalStrorage(BookID);
            string RequestURL = $"{CPWenKuRequestDefine.GetBookWithIDURL}{BookID}/";
            HtmlDocument? WebContent = this.CrawerService.FetchWebContent<HtmlDocument>(RequestURL);
            if (LocalResult != null && WebContent != null)
            {
                DateTime RemoteUpdateTime = DateTime.ParseExact(WebContent.DocumentNode.SelectSingleNode("//p[@class='booktime']").InnerText.Replace("更新时间：", ""), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                if (RemoteUpdateTime == LocalResult.UpdateTime)
                {
                    return LocalResult;
                }
            }
            if(WebContent != null)
            {
                BookModel ResultBookModel = this.ModelParseService.ParseBookModel(WebContent, BookID);
                this.LocalStrorageService.SaveBookModelToLocalStrorage(ResultBookModel);
                return ResultBookModel;
            }
            return null;
        }

        public BookContentModel? GetBookContentModel(int BookID)
        {
            BookModel? DestBookModel = this.GetBookModel(BookID);
            BookContentModel? LocalResult = this.LocalStrorageService.LoadBookContentModelFromLocalStrorage(BookID);
            if(LocalResult != null && LocalResult.BookModel != null && DestBookModel != null)
            {
                if (LocalResult != null)
                {
                    if (DestBookModel.UpdateTime == LocalResult.BookModel.UpdateTime)
                    {
                        return LocalResult;
                    }
                }
            }
            BookContentModel ResultBookContentModel = new BookContentModel();
            string BookModelRequestURL = $"{CPWenKuRequestDefine.GetBookWithIDURL}{BookID}/";
            if (DestBookModel != null && DestBookModel.ChapterList != null)
            {
                Dictionary<string, PageModel> CarwWebList = new Dictionary<string, PageModel>();
                foreach (string PageLink in DestBookModel.ChapterList)
                {
                    string RequestPageURL = $"{BookModelRequestURL}{PageLink}";
                    PageModel? BookPageModel = this.CrawerService.FetchWebContent<PageModel>(RequestPageURL);
                    if(BookPageModel != null)
                    {
                        CarwWebList.Add(RequestPageURL, BookPageModel);
                        if(!string.IsNullOrEmpty(BookPageModel.NextPageLink) && BookPageModel.NextPageLink != BookModelRequestURL && !CarwWebList.ContainsKey(BookPageModel.NextPageLink))
                        {
                            CarwWebList.Add(PageLink, BookPageModel);
                        }
                    }
                }
            }
            
            this.LocalStrorageService.SaveBookContentModelToLocalStrorage(ResultBookContentModel);
            return ResultBookContentModel;
        }

        public bool TryGetBookOrAddBookToCrawerQueue(BookModel book, bool Force = false)
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

            if(BookModelInstance.ChapterList != null)
            {
                for (int i = 0; i < BookModelInstance.ChapterList.Count; i++)
                {
                    string RequestURL = $"https://www.cpwenku.net/go/{BookModelInstance.ID}/{BookModelInstance.ChapterList[i]}";
                    HtmlDocument? WebContent = this.CrawerService.FetchWebContent< HtmlDocument>(RequestURL);
                    if (WebContent != null)
                    {
                        HtmlNode ContentNode = WebContent.DocumentNode.SelectSingleNode("//div[@class='readcontent']");
                        if(ContentNode != null)
                        {
                            ContentNode.RemoveChild(ContentNode.ChildNodes[0]);
                            ContentNode.RemoveChild(ContentNode.ChildNodes[0]);
                            ContentNode.RemoveChild(ContentNode.ChildNodes[ContentNode.ChildNodes.Count - 1]);
                            ContentNode.RemoveChild(ContentNode.ChildNodes[ContentNode.ChildNodes.Count - 1]);
                            Result.Add(ContentNode.InnerText);
                            this.Logger.LogInformation($"{BookModelInstance.ID} Processed {i + 1} / {BookModelInstance.ChapterList.Count}");
                        }
                    }
                }
                BookModelInstance.ChapterList = Result;
                string SerializedBookModel = JsonConvert.SerializeObject(BookModelInstance);
                string LocalBookPath = this.StorageDirectory + $"/cpwenku/Books/{BookModelInstance.ID}.json";
                System.IO.File.WriteAllText(LocalBookPath, SerializedBookModel);
                this.Logger.LogInformation($"Finish Processed {BookModelInstance.ID}");
            }
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

        private async Task ProcessBookDetail(BookModel BookDetailInstance)
        {
            // 实现你的异步逻辑
            await Task.Delay(TimeSpan.FromSeconds(1)); // 假设的异步操作
            List<string> Result = new List<string>();

            if(BookDetailInstance.ChapterList != null)
            {
                for (int i = 0; i < BookDetailInstance.ChapterList.Count; i++)
                {
                    string RequestURL = $"https://www.cpwenku.net/go/{BookDetailInstance.ID}/{BookDetailInstance.ChapterList[i]}";
                    HtmlDocument? WebContent = this.CrawerService.FetchWebContent<HtmlDocument>(RequestURL);
                    if(WebContent != null)
                    {
                        HtmlNode ContentNode = WebContent.DocumentNode.SelectSingleNode("//div[@class='readcontent']");
                        if (ContentNode != null)
                        {
                            ContentNode.RemoveChild(ContentNode.ChildNodes[0]);
                            ContentNode.RemoveChild(ContentNode.ChildNodes[0]);
                            ContentNode.RemoveChild(ContentNode.ChildNodes[ContentNode.ChildNodes.Count - 1]);
                            ContentNode.RemoveChild(ContentNode.ChildNodes[ContentNode.ChildNodes.Count - 1]);
                            Result.Add(ContentNode.InnerText);
                            this.Logger.LogInformation($"{BookDetailInstance.ID} Processed {i + 1} / {BookDetailInstance.ChapterList.Count}");
                        }
                    }
                }
                BookDetailInstance.ChapterList = Result;
                string SerializedBookDetail = JsonConvert.SerializeObject(BookDetailInstance);
                string LocalBookPath = this.StorageDirectory + $"/cpwenku/Books/{BookDetailInstance.ID}.json";
                System.IO.File.WriteAllText(LocalBookPath, SerializedBookDetail);
                this.Logger.LogInformation($"Finish Processed {BookDetailInstance.ID}");
            }
        }

        public bool IsBookModelEqual(BookModel Left, BookModel Right)
        {
            if(Left.UpdateTime == Right.UpdateTime)
            {
                return true;
            }
            return false;
        }
        public bool IsBookModelUpdateTimeEqual(BookModel Left, BookModel Right, bool CheckUpdateTime)
        {
            return Left.UpdateTime == Left.UpdateTime;
        }

        public bool IsCategoryModelCollectionEqual(List<CategoryModel> Left, List<CategoryModel> Right)
        {
            for (int i = 0; i < Left.Count; i++)
            {
                if (!IsCategoryModelEqual(Left[i], Right[i]))
                {
                    return false;
                }
            }
            return true;
        }
        public bool IsCategoryModelEqual(CategoryModel Left, CategoryModel Right)
        {
            if(Left.Name != Right.Name)
            {
                return false;
            }
            if(Left.Link != Right.Link)
            {
                return false;
            }
            if(Left.Books != null && Right.Books != null)
            {
                if(Left.Books.Count != Right.Books.Count)
                {
                    return false;
                }
                if(!IsBookModelCollectionEqual(Left.Books, Right.Books))
                {
                    return false;
                }
            }
            return true;
        }

        public bool IsBookModelCollectionEqual(List<BookModel> Left, List<BookModel> Right)
        {
            for (int i = 0; i < Left.Count; i++)
            {
                if (!IsBookModelEqual(Left[i], Right[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}