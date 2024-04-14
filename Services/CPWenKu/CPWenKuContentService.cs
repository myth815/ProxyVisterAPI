using HtmlAgilityPack;
using Newtonsoft.Json;
using ProxyVisterAPI.Models.CPWenku;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

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
        public float CraweProgress { get; set; }
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
        private ICrawerService CrawerService;
        private ICPWenKuModelParseService ModelParseService;
        private ICPWenKuLocalStrorageService LocalStrorageService;

        private readonly Mutex BookModelCraweLocker = new Mutex();
        private BookContentModel? CurrentCraweBookContent;
        private ConcurrentQueue<BookModel> CraweBookContentTaskList;
        private ConcurrentDictionary<string, PageModel?> PageModelAsyncFetchResult;

        public CPWenKuModelService(ILogger<CPWenKuModelService> ServiceLogger, ICrawerService CrawerService, ICPWenKuModelParseService ModelParseService, ICPWenKuLocalStrorageService localStrorageService)
        {
            this.Logger = ServiceLogger;
            this.CrawerService = CrawerService;
            this.ModelParseService = ModelParseService;
            this.LocalStrorageService = localStrorageService;

            this.CraweBookContentTaskList = new ConcurrentQueue<BookModel>();
            this.PageModelAsyncFetchResult = new ConcurrentDictionary<string, PageModel?>();
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
                        Result.CraweProgress = 1.0f;
                        return Result;
                    }
                }
            }
            if(DestBookModel != null)
            {
                this.BookModelCraweLocker.WaitOne();
                Result.SuccessGetModel = false;
                if (CurrentCraweBookContent != null && CurrentCraweBookContent.BookModel != null && CurrentCraweBookContent.BookModel.ID == BookID)
                {
                    //TODO 正在抓取中,获取进度
                    Result.CraweProgress = 1.0f;
                }
                else
                {
                    foreach (BookModel BookWaitForCrawe in this.CraweBookContentTaskList)
                    {
                        if (BookWaitForCrawe.ID == BookID)
                        {
                            Result.CraweProgress = 0.0f;
                            return Result;
                        }
                    }
                    this.CraweBookContentTaskList.Enqueue(DestBookModel);
                    this.FlushBookToCrawerList();
                }
                this.BookModelCraweLocker.ReleaseMutex();
                return Result;
            }
            return Result;
        }

        public void FlushBookToCrawerList()
        {
            if (this.CurrentCraweBookContent == null && this.CraweBookContentTaskList.TryDequeue(out BookModel? BookModel))
            {
                this.CraweBookContent(BookModel);
            }
        }

        public void CraweBookContent(BookModel Book)
        {
            this.CurrentCraweBookContent = new BookContentModel();
            this.PageModelAsyncFetchResult.Clear();
            this.CurrentCraweBookContent.BookModel = Book;
            string BookModelRequestURL = $"{CPWenKuRequestDefine.GetBookWithIDURL}{Book.ID}/";

            if (Book.ChapterList != null)
            {
                foreach (string PageLink in Book.ChapterList)
                {
                    string RequestPageURL = $"{BookModelRequestURL}{PageLink}";
                    if(this.PageModelAsyncFetchResult.TryAdd(RequestPageURL, null))
                    {
                        this.CrawerService.AsyncFetchWebContent<PageModel>(RequestPageURL, this.OnLoadBookPageModelCompleted);
                    }
                }
            }
        }

        public void OnLoadBookPageModelCompleted(AsyncWebFetchTask<PageModel> Result)
        {
            if (this.PageModelAsyncFetchResult.TryUpdate(Result.URL.OriginalString, Result.ResultWithType, null) && Result.ResultWithType != null && !string.IsNullOrEmpty(Result.ResultWithType.NextPageLink))
            {
                if(PageModelAsyncFetchResult.TryAdd(Result.ResultWithType.NextPageLink, null))
                {
                    this.CrawerService.AsyncFetchWebContent<PageModel>(Result.ResultWithType.NextPageLink, this.OnLoadBookPageModelCompleted);
                    return;
                }
            }
            //检查是否所有任务全部完成了
            if (!this.PageModelAsyncFetchResult.Values.Contains(null))
            {
                List<PageModel>? ListPageResult = this.CheckAllBookContentPageTaskCompleted();
                if(ListPageResult != null)
                {
                    this.OnBookModelCarweCompleted(ListPageResult);
                }
            }
        }

        public List<PageModel>? CheckAllBookContentPageTaskCompleted()
        {
            //检查是否都能顺序到最终
            if(this.CurrentCraweBookContent != null && this.CurrentCraweBookContent.BookModel != null && this.CurrentCraweBookContent.BookModel.ChapterList != null && this.CurrentCraweBookContent.BookModel.ChapterList.Count != 0)
            {
                List<PageModel> Result = new List<PageModel>();
                string FirstPageURL = this.CurrentCraweBookContent.BookModel.ChapterList[0];
                string FullFirstPageURL = $"{CPWenKuRequestDefine.GetBookWithIDURL}{this.CurrentCraweBookContent.BookModel.ID}/{FirstPageURL}";
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

        public void OnBookModelCarweCompleted(List<PageModel> ListPageResult)
        {
            this.Logger.LogInformation("CraweBookModelFinished");
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