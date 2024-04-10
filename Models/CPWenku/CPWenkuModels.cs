using HtmlAgilityPack;
using Newtonsoft.Json;
using ProxyVisterAPI.Services;
using System.Net;

namespace ProxyVisterAPI.Models.CPWenku
{
    public class DataModel
    {
        public T? ParseModel<T>(HtmlDocument HTMLContent)
        {
            return default;
        }
    }
    public class BookModel
    {
        public int ID { get; set; }
        public string? CoverPath { get; set; }
        public string? NoCoverPath { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public uint WordsCount { get; set; }
        public bool Finished { get; set; }
        public string? Introduction { get; set; }
        public DateTime UpdateTime { get; set; }
        public List<string>? ChapterList { get; set; }
    }

    public class PageModel
    {
        public uint ChapterNumber { get; set; }
        public uint CurrentPageNumber { get; set; }
        public uint TotolPageNumber { get; set; }
        public List<string>? ContentLines { get; set; }
        public string? PrivousPageLink { get; set; }
        public string? NextPageLink { get; set; }
        public bool IsEndOfBook { get; set; }
    }

    public class BookContentModel
    {
        public BookModel? BookModel { get; set; }
        public List<PageModel>? PageModels { get; set; }
    }

    public class CategoryModel
    {
        public string? Name { get; set; }
        public string? Link { get; set; }
        public List<BookModel>? Books { get; set; }
    }
}