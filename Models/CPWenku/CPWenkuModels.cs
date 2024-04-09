using Newtonsoft.Json;
using ProxyVisterAPI.Services;
using System.Net;

namespace ProxyVisterAPI.Models.CPWenku
{
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
        public List<PageModel>? PageList { get; set; }
        public List<string>? ContentLines { get; set; }
        public List<string>? Content { get; set; }
        public string? Link { get; set; }
        public DateTime? CrawlTime { get; set; }

        public void OverloadBookModel(BookModel Self)
        {
            this.ID = Self.ID;
            this.CoverPath = Self.CoverPath;
            this.NoCoverPath = Self.NoCoverPath;
            this.Title = Self.Title;
            this.Author = Self.Author;
            this.WordsCount = Self.WordsCount;
            this.Finished = Self.Finished;
            this.Introduction = Self.Introduction;
            this.UpdateTime = Self.UpdateTime;
            this.ChapterList = Self.ChapterList;
            this.PageList = Self.PageList;
            this.ContentLines = Self.ContentLines;
            this.Content = Self.Content;
            this.Link = Self.Link;
            this.CrawlTime = Self.CrawlTime;
        }
    }

    public class PageModel
    {
        public uint ChapterNumber { get; set; }
        public uint CurrentPageNumber { get; set; }
        public uint TotolPageNumber { get; set; }
        public List<string>? ContentLines { get; set; }
        public string? PrivousPageLink { get; set; }
        public string? NextPageLink { get; set; }
    }

    public class CategoryModel
    {
        public string? Name { get; set; }
        public string? Link { get; set; }
        public List<BookModel>? Books { get; set; }
    }
}