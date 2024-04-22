using HtmlAgilityPack;
using ProxyVisterAPI.Models.CPWenku;
using System.Collections.Concurrent;
using System.Globalization;

namespace ProxyVisterAPI.Services.CPWenKu
{
    public class CPWenKuRequestDefine
    {
        public const string DomainURL = "https://www.cpwenku.net";
        public const string CategoriesURL = $"{DomainURL}/all.html";
        public const string GetBooksWithauthorURL = $"{DomainURL}/modules/article/authorarticle.php?author=";
        public const string GetBookWithIDURL = $"{DomainURL}/go/";
    }

    public class ResultOfBookContentModel
    {
        public bool SuccessGetModel { get; set; }
        public uint CrawleTotalTasks { get; set; }
        public uint CrawleFinishedTasks { get; set; }
        public BookModel? BookModel { get; set; }
        public BookContentModel? BookContentModel { get; set; }
    }

    public interface ICPWenKuModelService
    {
        List<CategoryModel>? GetCategories(bool Force = false);
        List<BookModel>? GetBookListFromCategory(string Category, bool Force = false);
        List<BookModel>? GetBookListFromAuthor(string Author);
        BookModel? GetBookModel(int BookID);
        ResultOfBookContentModel GetBookContentModel(int BookID);
        bool IsBookModelEqual(BookModel Left, BookModel Right);
        bool IsBookModelUpdateTimeEqual(BookModel Left, BookModel Right, bool CheckUpdateTime);
        bool IsBookModelCollectionEqual(List<BookModel> Left, List<BookModel> Right);
        bool IsCategoryModelEqual(CategoryModel Left, CategoryModel Right);
    }

    public class CPWenKuModelService : ICPWenKuModelService
    {
        private ILogger<CPWenKuModelService> Logger;
        private ICrawlerService CrawlerService;
        private ICPWenKuModelParseService ModelParseService;
        private ICPWenKuLocalStrorageService LocalStrorageService;
        private ITextService TextService;

        private readonly Mutex BookModelCrawlerLocker = new Mutex();
        private BookContentModel? CurrentCrawlerBookContent;
        private ConcurrentQueue<BookModel> CrawlerBookContentTaskList;
        private ConcurrentDictionary<string, PageModel?> PageModelAsyncFetchResult;

        public CPWenKuModelService(ILogger<CPWenKuModelService> ServiceLogger, ICrawlerService CrawlerService, ICPWenKuModelParseService ModelParseService, ICPWenKuLocalStrorageService localStrorageService, ITextService TextService)
        {
            this.Logger = ServiceLogger;
            this.CrawlerService = CrawlerService;
            this.ModelParseService = ModelParseService;
            this.LocalStrorageService = localStrorageService;
            this.TextService = TextService;

            this.CrawlerBookContentTaskList = new ConcurrentQueue<BookModel>();
            this.PageModelAsyncFetchResult = new ConcurrentDictionary<string, PageModel?>();
        }

        public List<CategoryModel>? GetCategories(bool Force = false)
        {
            List<CategoryModel>? LocalResult = this.LocalStrorageService.LoadCategoryModelFromLocalStrorage();
            if(LocalResult == null || Force)
            {
                List<CategoryModel>? RemoteResult = this.CrawlerService.FetchWebContent<List<CategoryModel>>(CPWenKuRequestDefine.CategoriesURL);
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
                List<BookModel>? RemoteResult = this.CrawlerService.FetchWebContent<List<BookModel>>(CPWenKuRequestDefine.CategoriesURL);
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
            List<BookModel>? RemoteResult = this.CrawlerService.FetchWebContent<List<BookModel>>(RequestURL);
            return RemoteResult;
        }

        public BookModel? GetBookModel(int BookID)
        {
            BookModel? LocalResult = this.LocalStrorageService.LoadBookModelFromLocalStrorage(BookID);
            string RequestURL = $"{CPWenKuRequestDefine.GetBookWithIDURL}{BookID}/";
            HtmlDocument? WebContent = this.CrawlerService.FetchWebContent<HtmlDocument>(RequestURL);
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
                BookModel ResultBookModel = this.ModelParseService.ParseBookModel(WebContent);
                ResultBookModel.ID = BookID;
                this.LocalStrorageService.SaveBookModelToLocalStrorage(ResultBookModel);
                return ResultBookModel;
            }
            return null;
        }

        public ResultOfBookContentModel GetBookContentModel(int BookID)
        {
            BookModel? DestBookModel = this.GetBookModel(BookID);
            ResultOfBookContentModel Result = new ResultOfBookContentModel();
            Result.BookModel = DestBookModel;
            BookContentModel? LocalResult = this.LocalStrorageService.LoadBookContentModelFromLocalStrorage(BookID);
            if (LocalResult != null && LocalResult.BookModel != null && DestBookModel != null)
            {
                if (LocalResult != null)
                {
                    if (DestBookModel.UpdateTime == LocalResult.BookModel.UpdateTime)
                    {
                        Result.SuccessGetModel = true;
                        Result.BookContentModel = LocalResult;
                        if(LocalResult.PageModels != null)
                        {
                            Result.CrawleFinishedTasks = (uint)LocalResult.PageModels.Count;
                            Result.CrawleTotalTasks = (uint)LocalResult.PageModels.Count;
                        }
                        return Result;
                    }
                }
            }
            if(DestBookModel != null)
            {
                this.BookModelCrawlerLocker.WaitOne();
                Result.SuccessGetModel = false;
                if (CurrentCrawlerBookContent != null && CurrentCrawlerBookContent.BookModel != null && CurrentCrawlerBookContent.BookModel.ID == BookID)
                {
                    //TODO 正在抓取中,获取进度
                    Result.CrawleFinishedTasks = (uint)(this.PageModelAsyncFetchResult.Count - PageModelAsyncFetchResult.Count(kvp => kvp.Value == null));
                    Result.CrawleTotalTasks = (uint)this.PageModelAsyncFetchResult.Count;
                }
                else
                {
                    foreach (BookModel BookWaitForCrawle in this.CrawlerBookContentTaskList)
                    {
                        if (BookWaitForCrawle.ID == BookID)
                        {
                            if(BookWaitForCrawle.ChapterList != null)
                            {
                                Result.CrawleFinishedTasks = 0;
                                Result.CrawleTotalTasks = (uint)BookWaitForCrawle.ChapterList.Count;
                            }
                            return Result;
                        }
                    }
                    this.CrawlerBookContentTaskList.Enqueue(DestBookModel);
                    this.FlushBookToCrawlerList();
                }
                this.BookModelCrawlerLocker.ReleaseMutex();
                return Result;
            }
            return Result;
        }

        public void FlushBookToCrawlerList()
        {
            if (this.CurrentCrawlerBookContent == null && this.CrawlerBookContentTaskList.TryDequeue(out BookModel? BookModel))
            {
                this.CrawleBookContent(BookModel);
            }
        }

        public void CrawleBookContent(BookModel Book)
        {
            this.CurrentCrawlerBookContent = new BookContentModel();
            this.PageModelAsyncFetchResult.Clear();
            this.CurrentCrawlerBookContent.BookModel = Book;
            string BookModelRequestURL = $"{CPWenKuRequestDefine.GetBookWithIDURL}{Book.ID}/";

            if (Book.ChapterList != null)
            {
                foreach (string PageLink in Book.ChapterList)
                {
                    string RequestPageURL = $"{BookModelRequestURL}{PageLink}";
                    if(this.PageModelAsyncFetchResult.TryAdd(RequestPageURL, null))
                    {
                        this.CrawlerService.AsyncFetchWebContent<PageModel>(RequestPageURL, this.OnLoadBookPageModelCompleted);
                    }
                }
            }
        }

        public void OnLoadBookPageModelCompleted(AsyncWebFetchTask<PageModel> Result)
        {
            if (this.PageModelAsyncFetchResult.TryUpdate(Result.URL.OriginalString, Result.ResultWithType, null) && Result.ResultWithType != null && !string.IsNullOrEmpty(Result.ResultWithType.NextPageLink))
            {
                int FinishedTaskCount = this.PageModelAsyncFetchResult.Count - PageModelAsyncFetchResult.Count(kvp => kvp.Value == null);
                this.Logger.LogInformation($"Progress is {FinishedTaskCount}/{this.PageModelAsyncFetchResult.Count}");
                if(PageModelAsyncFetchResult.TryAdd(Result.ResultWithType.NextPageLink, null))
                {
                    this.CrawlerService.AsyncFetchWebContent<PageModel>(Result.ResultWithType.NextPageLink, this.OnLoadBookPageModelCompleted);
                    return;
                }
            }
            
            //检查是否所有任务全部完成了
            if (!this.PageModelAsyncFetchResult.Values.Contains(null))
            {
                List<PageModel>? ListPageResult = this.CheckAllBookContentPageTaskCompleted();
                if(ListPageResult != null)
                {
                    this.OnBookModelCarwleCompleted(ListPageResult);
                }
            }
        }

        public List<PageModel>? CheckAllBookContentPageTaskCompleted()
        {
            //检查是否都能顺序到最终
            if(this.CurrentCrawlerBookContent != null && this.CurrentCrawlerBookContent.BookModel != null && this.CurrentCrawlerBookContent.BookModel.ChapterList != null && this.CurrentCrawlerBookContent.BookModel.ChapterList.Count != 0)
            {
                List<PageModel> Result = new List<PageModel>();
                string FirstPageURL = this.CurrentCrawlerBookContent.BookModel.ChapterList[0];
                string FullFirstPageURL = $"{CPWenKuRequestDefine.GetBookWithIDURL}{this.CurrentCrawlerBookContent.BookModel.ID}/{FirstPageURL}";
                if(this.PageModelAsyncFetchResult.TryGetValue(FullFirstPageURL, out PageModel? FirstPage))
                {
                    PageModel? PageModel = FirstPage;
                    while(PageModel != null)
                    {
                        Result.Add(PageModel);
                        if(PageModel.IsEndOfBook)
                        {
                            break;
                        }
                        string? NextPageLink = PageModel.NextPageLink;
                        if (!string.IsNullOrEmpty(NextPageLink) && !this.PageModelAsyncFetchResult.TryGetValue(NextPageLink, out PageModel))
                        {
                            return null;
                        }
                    }
                    return Result;
                }
            }
            return null;
        }

        public void OnBookModelCarwleCompleted(List<PageModel> ListPageResult)
        {
            List<string> FinallResult = new List<string>();
            bool NeedLinkNextPage = false;
            for(int i = 0; i < ListPageResult.Count; i++)
            {
                PageModel CurrentPage = ListPageResult[i];
                if(CurrentPage.ContentLines != null)
                {
                    if (NeedLinkNextPage)
                    {
                        FinallResult[FinallResult.Count - 1] = FinallResult[FinallResult.Count - 1] + CurrentPage.ContentLines[0];
                    }
                    for (int j = 1; j < CurrentPage.ContentLines.Count; j++)
                    {
                        FinallResult.Add(CurrentPage.ContentLines[j]);
                    }
                    PageModel? NextPage = i + 1 < ListPageResult.Count ? ListPageResult[i + 1] : null;
                    if (NextPage != null && CurrentPage.ContentLines != null && NextPage.ContentLines != null && CurrentPage.ContentLines.Count > 0 && NextPage.ContentLines.Count > 0)
                    {
                        bool IsCompletedCurrentPage = this.TextService.IsParagraphComplete(CurrentPage.ContentLines[CurrentPage.ContentLines.Count - 1]);
                        bool IsCompletedNextPage = this.TextService.IsParagraphComplete(NextPage.ContentLines[0]);
                        if (!IsCompletedCurrentPage || !IsCompletedNextPage)
                        {
                            NeedLinkNextPage = true;
                        }
                    }
                }
            }
            if(this.CurrentCrawlerBookContent != null)
            {
                this.CurrentCrawlerBookContent.PageModels = ListPageResult;
                this.CurrentCrawlerBookContent.ContentLines = FinallResult;
                this.LocalStrorageService.SaveBookContentModelToLocalStrorage(this.CurrentCrawlerBookContent);
            }
            this.CurrentCrawlerBookContent = null;
            this.FlushBookToCrawlerList();
            this.Logger.LogInformation("CrawleBookModelFinished");
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