using HtmlAgilityPack;
using Newtonsoft.Json;
using ProxyVisterAPI.Models.CPWenku;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace ProxyVisterAPI.Services.CPWenKu
{
    public interface ICPWenKuModelParseService
    {
        List<CategoryModel> ParseCategoryModel(HtmlDocument CategoryHtmlDocument);
        List<BookModel> ParseBookModelCollection(HtmlDocument ChapterHtmlDocument);
        BookModel ParseBookModel(HtmlDocument BookHtmlDocument, int BookID);
        PageModel ParsePageModel(HtmlDocument ChapterHtmlDocument);
    }

    public class CPWenKuModelParseService : ICPWenKuModelParseService
    {
        ILogger<CPWenKuModelParseService> Logger;
        public CPWenKuModelParseService(ILogger<CPWenKuModelParseService> ServiceLogger)
        {
            this.Logger = ServiceLogger;
        }

        public List<CategoryModel> ParseCategoryModel(HtmlDocument CategoryHtmlDocument)
        {
            List<CategoryModel> Result = new List<CategoryModel>();
            HtmlNodeCollection HtmlNodes = CategoryHtmlDocument.DocumentNode.SelectNodes("//div[@class='class']//li//a");
            if (HtmlNodes != null)
            {
                foreach (HtmlNode Node in HtmlNodes)
                {
                    CategoryModel Category = new CategoryModel
                    {
                        Name = Node.InnerText,
                        Link = Node.GetAttributeValue("href", string.Empty)
                    };
                    Result.Add(Category);
                }
            }
            return Result;
        }

        public List<BookModel> ParseBookModelCollection(HtmlDocument ChapterHtmlDocument)
        {
            return new List<BookModel>();
        }

        public BookModel ParseBookModel(HtmlDocument BookHtmlDocument, int BookID)
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
                    if(PageTitle == $"第{ChapterNumber}节")
                    {
                        ChapterPageList.Add(PageLink);
                    }
                }
            }
            
            BookModel ResultBookDetail = new BookModel
            {
                ID = BookID,
                CoverPath = BookHtmlDocument.DocumentNode.SelectSingleNode("//img[@class='thumbnail']").Attributes["src"].Value,
                NoCoverPath = BookHtmlDocument.DocumentNode.SelectSingleNode("//img[@class='thumbnail']").Attributes["onerror"].Value,
                Title = BookInfo.SelectSingleNode("//h1[@class='booktitle']").InnerText,
                Author = BoolTag.SelectSingleNode("//a[@class='red']").InnerText,
                WordsCount = uint.Parse(BoolTag.SelectSingleNode("//span[@class='blue']").InnerText.Replace("万字", string.Empty)),
                Finished = BoolTag.SelectSingleNode("//span[@class='red']").InnerText == "已完结",
                Introduction = BookInfo.SelectSingleNode("//p[@class='bookintro']").InnerText,
                UpdateTime = DateTime.ParseExact(BookHtmlDocument.DocumentNode.SelectSingleNode("//p[@class='booktime']").InnerText.Replace("更新时间：", ""), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ChapterList = ChapterPageList
            };
            return ResultBookDetail;
        }

        public PageModel ParsePageModel(HtmlDocument ChapterHtmlDocument)
        {
            HtmlNode BookReadInfo = ChapterHtmlDocument.DocumentNode.SelectSingleNode("//div[@class='book read']");
            HtmlNode TitleInfo = BookReadInfo.SelectSingleNode("//h1[@class='pt10']");
            HtmlNode ChapterInfo = TitleInfo.ChildNodes[0];
            HtmlNode SmallTitleInfo = TitleInfo.ChildNodes[1];
            HtmlNode PageContent = BookReadInfo.SelectSingleNode("//div[@class='readcontent']");
            HtmlNode ButtomInfo = BookReadInfo.SelectSingleNode("//p[@class='text-center']");
            HtmlNode PreviousLink = ButtomInfo.SelectSingleNode("//a[@id='linkPrev']");
            HtmlNode NextLink = ButtomInfo.SelectSingleNode("//a[@id='linkNext']");
            HtmlNode NextIndex = ButtomInfo.SelectSingleNode("//a[@id='linkIndex']");

            List<string> ContentLines = new List<string>();
            for (int i = 0; i < PageContent.ChildNodes.Count; i++)
            {
                switch (PageContent.ChildNodes[i].Name)
                {
                    case "#text":
                        {
                            ContentLines.Add(PageContent.ChildNodes[i].InnerText);
                            break;
                        }
                    case "div":
                    case "br":
                    case "p":
                        {
                            break;
                        }
                    default:
                        {
                            this.Logger.LogWarning("Unknow Node Type: " + PageContent.ChildNodes[i].Name);
                            break;
                        }
                }
            }
            string ChapterPattern = @"第(\d+)节";
            string PagePattern = @"\((\d+)/(\d+)\)";
            Match MatchSmallTitle = Regex.Match(SmallTitleInfo.InnerText, PagePattern);
            if(!MatchSmallTitle.Success)
            {
                this.Logger.LogError(PagePattern + " Not Matched: " + SmallTitleInfo.InnerText);
            }
            Match MatchChapter = Regex.Match(ChapterInfo.InnerText, ChapterPattern);
            if(!MatchChapter.Success)
            {
                this.Logger.LogError(ChapterPattern + " Not Matched: " + ChapterInfo.InnerText);
            }
            PageModel ResultPageModel = new PageModel
            {
                ChapterNumber = uint.Parse(MatchChapter.Groups[1].Value),
                CurrentPageNumber = uint.Parse(MatchSmallTitle.Groups[1].Value),
                TotolPageNumber = uint.Parse(MatchSmallTitle.Groups[2].Value),
                ContentLines = ContentLines,
                PrivousPageLink = PreviousLink.Attributes["href"].Value,
                NextPageLink = NextLink.Attributes["href"].Value
            };
            if(NextIndex.Attributes["href"].Value == ResultPageModel.NextPageLink)
            {
                ResultPageModel.IsEndOfBook = true;
            }
            return ResultPageModel;
        }
    }
}