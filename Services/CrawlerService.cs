﻿using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace ProxyVisterAPI.Services
{
    public interface ICrawlerService
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

    public class DomainCrawlerManager
    {
        protected ILogger<CrawlerService> Logger;
        protected IConfigurationSection Configuration;
        IModelParserService ModelParserService;
        protected string DomainName;
        protected string? ProxyPoolUrl;
        protected uint MaxConnectPool;
        protected uint MaxTryCount;
        protected uint TimeOut;

        protected ConcurrentQueue<AsyncWebFetchTaskBase> CrawlerTaskList;
        protected ConcurrentStack<HttpClient> CrawlerClientStack;
        public DomainCrawlerManager(ILogger<CrawlerService> Logger, IModelParserService ModelParserService, string DomainName, IConfigurationSection Configuration)
        {
            this.Logger = Logger;
            this.ModelParserService = ModelParserService;
            this.DomainName = DomainName;
            this.Configuration = Configuration;
            this.MaxConnectPool = this.Configuration.GetValue<uint>("MaxConnectPool");
            this.MaxTryCount = this.Configuration.GetValue<uint>("MaxTryCount");
            this.TimeOut = this.Configuration.GetValue<uint>("TimeOut");
            this.ProxyPoolUrl = this.Configuration.GetValue<string>("ProxyPool");
            this.CrawlerTaskList = new ConcurrentQueue<AsyncWebFetchTaskBase>();
            this.CrawlerClientStack = new ConcurrentStack<HttpClient>();
        }

        public void AsyncFetchWebContent<T>(Uri UriRequest, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback)
        {
            AsyncWebFetchTask<T> Task = new AsyncWebFetchTask<T>(UriRequest, FinishCallback);
            this.CrawlerTaskList.Enqueue(Task);
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
                HttpClient WebClient = this.GetHttpClientWithProxy();
                HttpRequestMessage Request = this.UpdateUriRequest(UriRequest);
                try
                {
                    HttpResponseMessage Response = WebClient.SendAsync(Request).GetAwaiter().GetResult();
                    switch (Response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            {
                                HtmlDocument WebContent = new HtmlDocument();
                                string ResponseContent = Response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                WebContent.LoadHtml(ResponseContent);
                                if (WebContent.DocumentNode.InnerText.Length != 0)
                                {
                                    this.CrawlerClientStack.Push(WebClient);
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
                                    continue;
                                }
                            }
                        default:
                            {
                                continue;
                            }
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        protected void FlushTaskQueue()
        {
            if(this.CrawlerTaskList.TryDequeue(out AsyncWebFetchTaskBase? Task))
            {
                HttpClient Client = this.GetHttpClientWithProxy();
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
                                if (WebContent.DocumentNode.InnerText.Length == 0)
                                {
                                    OnTaskFetchFail(Task);
                                    break;
                                }
                                else
                                {
                                    this.CrawlerClientStack.Push(Client);
                                    Task.Result = WebContent;
                                    Task.StringResult = WebStringCOntent;
                                    Task.HtmlResult = WebContent;
                                    Type SourceType = Task.GetType();
                                    Type[] ReturnTypes = SourceType.GetGenericArguments();
                                    if (ReturnTypes.Length == 1)
                                    {
                                        Type ReturnTypeOfModel = ReturnTypes[0];
                                        Type ModelParserServiceType = typeof(ModelParserService);
                                        MethodInfo? ParseModelMethod = ModelParserServiceType.GetMethod("ParseModel");
                                        if (ParseModelMethod == null)
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
                                            TaskCallbackFunctionMethod.Invoke(Task, new object[] { });
                                        }
                                    }
                                    this.Logger.LogInformation($"Task {Task.URL.OriginalString} Completed");
                                    this.FlushTaskQueue();
                                    break;
                                }
                            }
                        case HttpStatusCode.Forbidden:
                            {
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
        }

        protected void OnTaskFetchFail(AsyncWebFetchTaskBase Task)
        {
            Task.RryCount++;
            if (Task.RryCount < MaxTryCount)
            {
                this.Logger.LogDebug($"ParseModel Fail with URL({Task.URL}), Retry");
                this.CrawlerTaskList.Enqueue(Task);
                this.FlushTaskQueue();
                return;
            }
            else
            {
                new Exception("Is Max Error Count");
            }
        }

        class ProxyInfo
        {
            public string anonymous = string.Empty;
            public int check_count = 0;
            public int fail_count = 0;
            public bool https = false;
            public bool last_status = false;
            public DateTime last_time = new DateTime();
            public string proxy = string.Empty;
            public string region = string.Empty;
            public string source = string.Empty;
        }

        protected HttpClient GetHttpClientWithProxy()
        {
            while (true)
            {
                if (this.CrawlerClientStack.TryPop(out HttpClient? WebClient))
                {
                    return WebClient;
                }
                else
                {
                    HttpResponseMessage Response = new HttpClient().GetAsync(this.ProxyPoolUrl).GetAwaiter().GetResult();
                    if (Response.StatusCode == HttpStatusCode.OK)
                    {
                        string ResponseContent = Response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        ProxyInfo? ProxyAgentInfo = JsonConvert.DeserializeObject<ProxyInfo>(ResponseContent);
                        if (ProxyAgentInfo != null && !string.IsNullOrEmpty(ProxyAgentInfo.proxy))
                        {
                            string ProxyAddress = (ProxyAgentInfo.https ? "https" : "http") + "://" + ProxyAgentInfo.proxy;
                            WebProxy WebProxySetting = new WebProxy(ProxyAddress);
                            this.Logger.LogInformation($"Get New Proxy( {ProxyAddress} )");
                            HttpClientHandler httpClientHandler = new HttpClientHandler
                            {
                                Proxy = WebProxySetting,
                                UseProxy = true,
                                UseCookies = false,
                            };
                            return new HttpClient(httpClientHandler);
                        }
                    }
                }
                Thread.Sleep(1000);
            }
        }
    }

    public class CrawlerService : ICrawlerService
    {
        private readonly ILogger<CrawlerService> Logger;
        private readonly IConfigurationSection Configure;
        private readonly IModelParserService ModelParserService;
        private Dictionary<string, DomainCrawlerManager> DomainCrawlerManagers;
        public CrawlerService(ILogger<CrawlerService> Logger, IConfiguration Configuration, IModelParserService ModelParserService)
        {
            this.Logger = Logger;
            this.DomainCrawlerManagers = new Dictionary<string, DomainCrawlerManager>();
            this.Configure = Configuration.GetSection("Services").GetSection("Carwer");
            this.ModelParserService = ModelParserService;
        }

        public void AddDomainCrawlerManager(string DomainName)
        {
            //查找对应配置
            IConfigurationSection? DomainCrawlerConfigure = Configure.GetSection($"DomainCrawlers:{DomainName}");
            DomainCrawlerManagers.Add(DomainName, new DomainCrawlerManager(Logger, ModelParserService, DomainName, DomainCrawlerConfigure));
        }

        private DomainCrawlerManager? GetDomainCrawlerManager(Uri UriRequest)
        {
            if (!DomainCrawlerManagers.ContainsKey(UriRequest.Host))
            {
                AddDomainCrawlerManager(UriRequest.Host);
            }
            return DomainCrawlerManagers[UriRequest.Host];
        }

        public T? FetchWebContent<T>(string RequestURL)
        {
            Uri UriRequest = new Uri(RequestURL);
            DomainCrawlerManager? Manager = GetDomainCrawlerManager(UriRequest);
            if (Manager != null)
            {
                return Manager.FetchWebContent<T>(UriRequest);
            }
            throw new Exception($"Can't Get DomainName From RequestURL( {UriRequest.OriginalString} )");
        }

        public void AsyncFetchWebContent<T>(string RequestURL, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback)
        {
            Uri UriRequest = new Uri(RequestURL);
            DomainCrawlerManager? Manager = GetDomainCrawlerManager(UriRequest);
            if (Manager == null)
            {
                throw new Exception("Can't Get DomainName From RequestURL( " + RequestURL + " )");
            }
            Manager.AsyncFetchWebContent(UriRequest, FinishCallback);
        }
    }
}