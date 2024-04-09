using HtmlAgilityPack;
using ProxyVisterAPI.Controllers;
using ProxyVisterAPI.Models.CPWenku;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace ProxyVisterAPI.Services
{
    public interface ICrawerService
    {
        Task<HtmlDocument> GetWebContent(string RequestURL);
    }

    public class CrawerService : ICrawerService
    {
        public int VistIntervals = 1;
        private IHttpClientFactory? _httpClientFactory;
        private WebProxy? WebProxySettings;
        private readonly ILogger<CrawerService> Logger;
        private Task? CurrentTask;
        private int CurrentTaskBookID;
        private ConcurrentQueue<string> CrawerTaskList;

        public CrawerService(ILogger<CrawerService> Logger)
        {
            this.Logger = Logger;
            this.CrawerTaskList = new ConcurrentQueue<string>();
            SetupProxy();
        }
        private void SetupProxy()
        {
            string? ProxyVistIntervals = Environment.GetEnvironmentVariable("ProxyVistIntervals");
            if (!string.IsNullOrEmpty(ProxyVistIntervals))
            {
                VistIntervals = int.Parse(ProxyVistIntervals);
                if (VistIntervals <= 0)
                {
                    this.Logger.LogInformation("ProxyVistIntervals Is Set Wrong Value( " + ProxyVistIntervals + " ), Please check. Now is 1 Default)");
                    VistIntervals = 1;
                }
            }
            else
            {
                this.Logger.LogInformation("ProxyVistIntervals Is Not Set, Please check. Now is 1 Default)");
                VistIntervals = 1;
            }
            string? HttpProxyURL = Environment.GetEnvironmentVariable("HTTP_PROXY");
            string? HttpsProxyURL = Environment.GetEnvironmentVariable("HTTPS_PROXY");
            string pattern = @"^(https?)://(([^:@]+):([^:@]+)@)?([^:/]+)(:([0-9]+))?$";
            if (string.IsNullOrEmpty(HttpProxyURL) && string.IsNullOrEmpty(HttpsProxyURL))
            {
                this.Logger.LogInformation("Disable Proxy.)");
            }
            else if (!string.IsNullOrEmpty(HttpsProxyURL))
            {
                Match match = Regex.Match(HttpsProxyURL, pattern);
                if (match.Success)
                {
                    string protocol = match.Groups[1].Value;
                    string username = match.Groups[3].Value;
                    string password = match.Groups[4].Value;
                    string ipOrDomain = match.Groups[5].Value;
                    int port = int.Parse(match.Groups[7].Value);
                    if (protocol == "https" && port > 0)
                    {
                        this.WebProxySettings = new WebProxy(protocol + "://" + ipOrDomain + ":" + port);
                        if (!string.IsNullOrEmpty(username))
                        {
                            this.WebProxySettings.Credentials = new NetworkCredential(username, password);
                        }
                    }
                    else
                    {
                        this.Logger.LogInformation("Failed Enable Proxy With URL( " + HttpsProxyURL + " ), Please check.)");
                    }
                }
            }
            else if (!string.IsNullOrEmpty(HttpProxyURL))
            {
                Match match = Regex.Match(HttpProxyURL, pattern);
                if (match.Success)
                {
                    string protocol = match.Groups[1].Value;
                    string username = match.Groups[3].Value;
                    string password = match.Groups[4].Value;
                    string ipOrDomain = match.Groups[5].Value;
                    int port = int.Parse(match.Groups[7].Value);
                    if (protocol == "http" && port > 0)
                    {
                        this.WebProxySettings = new WebProxy(protocol + "://" + ipOrDomain + ":" + port);
                        if (!string.IsNullOrEmpty(username))
                        {
                            this.WebProxySettings.Credentials = new NetworkCredential(username, password);
                        }
                    }
                }
                else
                {
                    this.Logger.LogInformation("Failed Enable Proxy With URL( " + HttpsProxyURL + " ), Please check.)");
                }
            }
        }

        private HttpClient GetHttpClientWithProxy()
        {
            HttpClientHandler httpClientHandler = new HttpClientHandler
            {
                Proxy = this.WebProxySettings,
                UseProxy = this.WebProxySettings != null,
            };

            // 注意：直接实例化HttpClient可能不是最佳实践，具体情况具体分析
            return new HttpClient(httpClientHandler);
        }

        public async Task<HtmlDocument> GetWebContent(string RequestURL)
        {
            while (true)
            {
                HttpClient client = GetHttpClientWithProxy();
                HttpResponseMessage Response = await client.GetAsync(RequestURL);
                if (Response.StatusCode == HttpStatusCode.OK)
                {
                    HtmlDocument WebContent = new HtmlDocument();
                    WebContent.LoadHtml(await Response.Content.ReadAsStringAsync());
                    return WebContent;
                }
                else
                {
                    Thread.Sleep(VistIntervals);
                    continue;
                }
            }
        }
    }
}
