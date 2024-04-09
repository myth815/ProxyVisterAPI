using HtmlAgilityPack;
using Newtonsoft.Json;
using ProxyVisterAPI.Models.CPWenku;
using System.Globalization;
using System.Net;

namespace ProxyVisterAPI.Services.CPWenKu
{
    public interface ICPWenKuModelParseService
    {
        CategoryModel ParseCategoryModel(HtmlDocument CategoryHtmlDocument);
        List<BookModel> ParseCollection(HtmlDocument ChapterHtmlDocument);
        BookModel ParseBookModel(HtmlDocument BookHtmlDocument);
        PageModel ParsePageModel(HtmlDocument ChapterHtmlDocument);
    }

    public class CPWenKuModelParserService : ICPWenKuModelParseService
    {
        Logger<CPWenKuModelParserService> Logger;
        public CPWenKuModelParserService(Logger<CPWenKuModelParserService> ServiceLogger)
        {
            this.Logger = ServiceLogger;
        }

        public CategoryModel ParseCategoryModel(HtmlDocument CategoryHtmlDocument)
        {
            return new CategoryModel();
        }

        public List<BookModel> ParseCollection(HtmlDocument ChapterHtmlDocument)
        {
            return new List<BookModel>();
        }

        public BookModel ParseBookModel(HtmlDocument BookHtmlDocument)
        {
            HtmlNode BookInfo = BookHtmlDocument.DocumentNode.SelectSingleNode("//div[@class='bookinfo']");
            HtmlNode BoolTag = BookInfo.ChildNodes[1];
            HtmlNode ListChapterAll = BookHtmlDocument.DocumentNode.SelectSingleNode("//div[@id='list-chapterAll']");
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

            BookModel ResultBookDetail = new BookModel
            {
                CoverPath = BookHtmlDocument.DocumentNode.SelectSingleNode("//img[@class='thumbnail']").Attributes["src"].Value,
                NoCoverPath = BookHtmlDocument.DocumentNode.SelectSingleNode("//img[@class='thumbnail']").Attributes["onerror"].Value,
                Title = BookInfo.SelectSingleNode("//h1[@class='booktitle']").InnerText,
                Author = BoolTag.SelectSingleNode("//a[@class='red']").InnerText,
                Finished = BoolTag.SelectSingleNode("//span[@class='red']").InnerText == "已完结",
                Introduction = BookInfo.SelectSingleNode("//p[@class='bookintro']").InnerText,
                UpdateTime = DateTime.ParseExact(BookHtmlDocument.DocumentNode.SelectSingleNode("//p[@class='booktime']").InnerText.Replace("更新时间：", ""), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
            return ResultBookDetail;
        }

        public PageModel ParsePageModel(HtmlDocument ChapterHtmlDocument)
        {
            return new PageModel();
        }
    }
}