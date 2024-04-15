using HtmlAgilityPack;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using ProxyVisterAPI.Controllers;
using ProxyVisterAPI.Models.CPWenku;
using ProxyVisterAPI.Services.CPWenKu;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using static ProxyVisterAPI.Services.DomainCrawerManager;

namespace ProxyVisterAPI.Services
{
    public interface ICrawerService
    {
        T? FetchWebContent<T>(string url);
        void AsyncFetchWebContent<T>(string RequestURL, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback);
    }

    public class AsyncWebFetchTaskBase
    {
        public Uri URL { get; set; }
        public string? StringResult { get; set; }
        public HtmlDocument? HtmlResult { get; set; }
        public object? Result { get; set; }
        public uint RryCount { get; set; }
        public Exception? exception { get; set; }

        public AsyncWebFetchTaskBase(Uri UriRequest)
        {
            this.URL = UriRequest;
        }
    }

    public class AsyncWebFetchTask<T> : AsyncWebFetchTaskBase
    {
        public delegate void TaskCompletedCallback(AsyncWebFetchTask<T> result);
        public delegate ModelType ParseModel<ModelType>(HtmlDocument HTMLContent);
        public T? ResultWithType { get; set; }
        public TaskCompletedCallback TaskCallback { get; set; }

        public AsyncWebFetchTask(Uri UriRequest, TaskCompletedCallback TaskCallback):base(UriRequest)
        {
            this.TaskCallback = TaskCallback;
        }

        public void InvokCallback()
        {
            if(this.Result != null)
            {
                this.ResultWithType = (T)Result;
            }
            this.TaskCallback.Invoke(this);
        }
    }

    public class DomainCrawerManager
    {
        protected ILogger<CrawerService> Logger;
        protected IConfigurationSection Configuration;
        IModelParserService ModelParserService;
        protected string DomainName;
        protected WebProxy? WebProxySettings;
        protected bool EnableProxy;
        protected int VistIntervals;
        protected uint MaxConnectPool;
        protected uint MaxTryCount;
        protected uint TimeOut;

        protected ConcurrentQueue<AsyncWebFetchTaskBase> CrawerTaskList;
        protected ConcurrentStack<HttpClient> HttpClientPool;
        public DomainCrawerManager(ILogger<CrawerService> Logger, IModelParserService ModelParserService, string DomainName, IConfigurationSection Configuration)
        {
            this.Logger = Logger;
            this.ModelParserService = ModelParserService;
            this.DomainName = DomainName;
            this.Configuration = Configuration;
            this.VistIntervals = this.Configuration.GetValue<int>("VistIntervals");
            this.MaxConnectPool = this.Configuration.GetValue<uint>("MaxConnectPool");
            this.MaxTryCount = this.Configuration.GetValue<uint>("MaxTryCount");
            this.TimeOut = this.Configuration.GetValue<uint>("TimeOut");
            this.SetupProxy();
            this.CrawerTaskList = new ConcurrentQueue<AsyncWebFetchTaskBase>();
            this.HttpClientPool = new ConcurrentStack<HttpClient>();
            for(int i = 0;i<MaxConnectPool;i++)
            {
                this.HttpClientPool.Push(this.GetHttpClientWithProxy());
            }
        }

        public void CheckVistIntervalsTime(bool Success)
        {
            if(Success && this.VistIntervals != 0)
            {
                this.VistIntervals = 0;
                this.Logger.LogInformation($"Change Vist Interval Time ( {this.VistIntervals} )");
            }
            else
            {
                this.VistIntervals += 10000;
                this.Logger.LogInformation($"Change Vist Interval Time ( {this.VistIntervals} ) And Sleeping!");
                Thread.Sleep(this.VistIntervals);
            }
        }

        public void AsyncFetchWebContent<T>(Uri UriRequest, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback)
        {
            AsyncWebFetchTask<T> Task = new AsyncWebFetchTask<T>(UriRequest, FinishCallback);
            this.CrawerTaskList.Enqueue(Task);
            this.Logger.LogInformation($"Add Async Task {UriRequest.OriginalString}");
            this.FlushTaskQueue();
        }

        private static readonly List<string> HeadList = [
            "Mozilla/5.0 (Windows NT 6.3; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/39.0.2171.95 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_2) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.1916.153 Safari/537.36",
            "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:30.0) Gecko/20100101 Firefox/30.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_9_2) AppleWebKit/537.75.14 (KHTML, like Gecko) Version/7.0.3 Safari/537.75.14",
            "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; Win64; x64; Trident/6.0)",
            "Mozilla/5.0 (Windows; U; Windows NT 5.1; it; rv:1.8.1.11) Gecko/20071127 Firefox/2.0.0.11",
            "Opera/9.25 (Windows NT 5.1; U; en)",
            "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; SV1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)",
            "Mozilla/5.0 (compatible; Konqueror/3.5; Linux) KHTML/3.5.5 (like Gecko) (Kubuntu)",
            "Mozilla/5.0 (X11; U; Linux i686; en-US; rv:1.8.0.12) Gecko/20070731 Ubuntu/dapper-security Firefox/1.5.0.12",
            "Lynx/2.8.5rel.1 libwww-FM/2.14 SSL-MM/1.4.1 GNUTLS/1.2.9",
            "Mozilla/5.0 (X11; Linux i686) AppleWebKit/535.7 (KHTML, like Gecko) Ubuntu/11.04 Chromium/16.0.912.77 Chrome/16.0.912.77 Safari/535.7",
            "Mozilla/5.0 (X11; Ubuntu; Linux i686; rv:10.0) Gecko/20100101 Firefox/10.0"
        ];

        public HttpRequestMessage UpdateUriRequest(Uri UriRequest)
        {
            HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Get, UriRequest.OriginalString);
            Request.Headers.Add("User-Agent", HeadList[new Random().Next(HeadList.Count)]);
            Request.Headers.Add("Referer", $"https://{this.DomainName}/");
            Request.Headers.Add("Host", this.DomainName);

            Request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            Request.Headers.Pragma.Add(new System.Net.Http.Headers.NameValueHeaderValue("no-cache"));
            return Request;
        }

        public T? FetchWebContent<T>(Uri UriRequest)
        {
            while (true)
            {
                HttpClient WebClient = GetHttpClientWithProxy();
                HttpRequestMessage Request = this.UpdateUriRequest(UriRequest);
                HttpResponseMessage Response = WebClient.SendAsync(Request).GetAwaiter().GetResult();
                switch (Response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        {
                            HtmlDocument WebContent = new HtmlDocument();
                            string ResponseContent = Response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            WebContent.LoadHtml(ResponseContent);
                            if(WebContent.DocumentNode.InnerText.Length != 0)
                            {
                                this.CheckVistIntervalsTime(true);
                                if (typeof(T) == typeof(HtmlDocument))
                                {
                                    T Result = (T)Convert.ChangeType(WebContent, typeof(T));
                                    return Result;
                                }
                                else
                                {
                                    return this.ModelParserService.ParseModel<T>(WebContent);
                                }
                            }
                            else
                            {
                                WebClient.Dispose();
                                this.CheckVistIntervalsTime(false);
                                continue;
                            }
                        }
                    default:
                        {
                            WebClient.Dispose();
                            this.CheckVistIntervalsTime(false);
                            continue;
                        }
                }
            }
        }

        protected void FlushTaskQueue()
        {
            if (this.HttpClientPool.TryPop(out HttpClient? Client))
            {
                if(!this.CrawerTaskList.TryDequeue(out AsyncWebFetchTaskBase? Task))
                {
                    this.HttpClientPool.Push(Client);
                    return;
                }
                this.Logger.LogDebug(DomainName + " Start Task " + Task.URL.OriginalString);
                HttpRequestMessage Request = this.UpdateUriRequest(Task.URL);
                Task<HttpResponseMessage> TaskResponse = Client.SendAsync(Request);
                TaskResponse.ContinueWith(TaskResponse =>
                {
                    this.Logger.LogDebug(DomainName + " End Task " + Task.URL.OriginalString);
                    HttpResponseMessage Response = TaskResponse.Result;
                    HttpStatusCode ResponseStateCode = Response.StatusCode;
                    switch (ResponseStateCode)
                    {
                        case HttpStatusCode.OK:
                            {
                                HtmlDocument WebContent = new HtmlDocument();
                                string WebStringCOntent = Response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                WebContent.LoadHtml(WebStringCOntent);
                                if(WebContent.DocumentNode.InnerText.Length == 0)
                                {
                                    Client.Dispose();
                                    OnTaskFetchFail(Task);
                                    break;
                                }
                                else
                                {
                                    this.CheckVistIntervalsTime(true);
                                    Task.Result = WebContent;
                                    Task.StringResult = WebStringCOntent;
                                    Task.HtmlResult = WebContent;
                                    Type SourceType = Task.GetType();
                                    Type[] ReturnTypes = SourceType.GetGenericArguments();
                                    if(ReturnTypes.Length == 1)
                                    {
                                        Type ReturnTypeOfModel = ReturnTypes[0];
                                        Type ModelParserServiceType = typeof(ModelParserService);
                                        MethodInfo? ParseModelMethod = ModelParserServiceType.GetMethod("ParseModel");
                                        if(ParseModelMethod == null)
                                        {
                                            throw new Exception();
                                        }
                                        else
                                        {
                                            MethodInfo Generic = ParseModelMethod.MakeGenericMethod(ReturnTypeOfModel);
                                            Task.Result = Generic.Invoke(this.ModelParserService, new object[] { WebContent });
                                        }
                                        
                                        Type TaskType = Task.GetType();
                                        MethodInfo? TaskCallbackFunctionMethod = TaskType.GetMethod("InvokCallback");
                                        if (TaskCallbackFunctionMethod == null)
                                        {
                                            throw new Exception();
                                        }
                                        else
                                        {
                                            TaskCallbackFunctionMethod.Invoke(Task, new object[] {});
                                        }
                                    }
                                    this.Logger.LogInformation($"Task {Task.URL.OriginalString} Completed With {Response.StatusCode}");
                                    this.HttpClientPool.Push(Client);
                                    this.FlushTaskQueue();
                                    break;
                                }
                            }
                        case HttpStatusCode.Forbidden:
                            {
                                Client.Dispose();
                                this.OnTaskFetchFail(Task);
                                break;
                            }
                        default:
                            {
                                Client.Dispose();
                                this.OnTaskFetchFail(Task);
                                break;
                            }
                    }
                },
                TaskContinuationOptions.OnlyOnRanToCompletion);
            }
            else
            {
                if(Client != null)
                {
                    this.HttpClientPool.Push(Client);
                }
            }
        }

        protected void OnTaskFetchFail(AsyncWebFetchTaskBase Task)
        {
            Task.RryCount++;
            if (Task.RryCount < MaxTryCount)
            {
                this.Logger.LogDebug($"ParseModel Fail with URL({Task.URL}), Retry");
                this.CheckVistIntervalsTime(false);
                this.CrawerTaskList.Enqueue(Task);
                this.HttpClientPool.Push(this.GetHttpClientWithProxy());
                this.FlushTaskQueue();
                return;
            }
            else
            {
                new Exception("Is Max Error Count");
            }
        }

        protected HttpClient GetHttpClientWithProxy()
        {
            HttpClientHandler httpClientHandler = new HttpClientHandler
            {
                Proxy = this.WebProxySettings,
                UseProxy = this.EnableProxy,
                UseCookies = false,
            };

            // 注意：直接实例化HttpClient可能不是最佳实践，具体情况具体分析
            return new HttpClient(httpClientHandler);
        }

        protected void SetupProxy()
        {
            IConfigurationSection ProxySection = this.Configuration.GetSection("Proxy");
            if (ProxySection.Exists())
            {
                string? ProxyProtol = ProxySection.GetValue<string>("Protol");
                string? ProxyHost = ProxySection.GetValue<string>("Host");
                uint? ProxyPort = ProxySection.GetValue<uint>("Port");
                if (string.IsNullOrEmpty(ProxyProtol) || string.IsNullOrEmpty(ProxyHost))
                {
                    this.Logger.LogError("ProxyProtol or ProxyHost Proxy Configure Is Not Set, Please check.)");
                }
                if (ProxyPort == 0)
                {
                    this.Logger.LogError("ProxyPort Proxy Configure Is 0, Please check.)");
                }
                string ProxyUri = $"{ProxyProtol}://{ProxyHost}:{ProxyPort}";
                this.WebProxySettings = new WebProxy(ProxyUri);
                string? ProxyUserName = ProxySection.GetValue<string>("UserName");
                string? ProxyPassword = ProxySection.GetValue<string>("Password");
                if (!string.IsNullOrEmpty(ProxyUserName))
                {
                    this.WebProxySettings.Credentials = new NetworkCredential(ProxyUserName, ProxyPassword);
                }
                this.EnableProxy = ProxySection.GetValue<bool>("Enable");
            }
        }
    }

    public class CrawerService : ICrawerService
    {
        private readonly ILogger<CrawerService> Logger;
        private readonly IConfigurationSection Configure;
        private readonly IModelParserService ModelParserService;
        private Dictionary<string, DomainCrawerManager> DomainCrawerManagers;
        public CrawerService(ILogger<CrawerService> Logger, IConfiguration Configuration, IModelParserService ModelParserService)
        {
            this.Logger = Logger;
            this.DomainCrawerManagers = new Dictionary<string, DomainCrawerManager>();
            this.Configure = Configuration.GetSection("Services").GetSection("Carwer");
            this.ModelParserService = ModelParserService;
        }

        public void AddDomainCrawerManager(string DomainName)
        {
            //查找对应配置
            IConfigurationSection? DomainCrawerConfigure = Configure.GetSection($"DomainCrawers:{DomainName}");
            DomainCrawerManagers.Add(DomainName, new DomainCrawerManager(Logger, ModelParserService, DomainName, DomainCrawerConfigure));
        }

        private DomainCrawerManager? GetDomainCrawerManager(Uri UriRequest)
        {
            if (!DomainCrawerManagers.ContainsKey(UriRequest.Host))
            {
                AddDomainCrawerManager(UriRequest.Host);
            }
            return DomainCrawerManagers[UriRequest.Host];
        }

        public T? FetchWebContent<T>(string RequestURL)
        {
            Uri UriRequest = new Uri(RequestURL);
            DomainCrawerManager? Manager = GetDomainCrawerManager(UriRequest);
            if (Manager != null)
            {
                return Manager.FetchWebContent<T>(UriRequest);
            }
            throw new Exception($"Can't Get DomainName From RequestURL( {UriRequest.OriginalString} )");
        }

        public void AsyncFetchWebContent<T>(string RequestURL, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback)
        {
            Uri UriRequest = new Uri(RequestURL);
            DomainCrawerManager? Manager = GetDomainCrawerManager(UriRequest);
            if (Manager == null)
            {
                throw new Exception("Can't Get DomainName From RequestURL( " + RequestURL + " )");
            }
            Manager.AsyncFetchWebContent(UriRequest, FinishCallback);
        }
    }
}