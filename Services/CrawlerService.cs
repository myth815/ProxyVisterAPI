using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

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

    public class DomainCrawlerManager
    {
        protected ILogger<CrawlerService> Logger;
        protected IConfigurationSection Configuration;
        IModelParserService ModelParserService;
        protected string DomainName;
        protected string? ProxyPoolUrl;
        protected uint MaxConnectPool;
        protected uint MaxTryCount;
        protected readonly List<string>? RequestUserAgentList;

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
            this.ProxyPoolUrl = this.Configuration.GetValue<string>("ProxyPool");
            this.RequestUserAgentList = this.Configuration.GetValue<List<string>>("RequestUserAgentList");
            this.CrawlerTaskList = new ConcurrentQueue<AsyncWebFetchTaskBase>();
            this.CrawlerClientStack = new ConcurrentStack<HttpClient>();
        }

        protected bool TryGetHttpClientWithProxy([MaybeNullWhen(false)] out HttpClient HttpClientWithProxy)
        {
            if (this.CrawlerClientStack.TryPop(out HttpClient? WebClient))
            {
                HttpClientWithProxy = WebClient;
                return true;
            }
            else
            {
                ProxyInfo? ProxyAgentInfo = null;
                try
                {
                    HttpClient CurlProxyPoolClient = new HttpClient();
                    HttpResponseMessage CurlProxyPoolResponse = CurlProxyPoolClient.GetAsync(this.ProxyPoolUrl).GetAwaiter().GetResult();
                    if (CurlProxyPoolResponse.StatusCode == HttpStatusCode.OK)
                    {
                        string ResponseContent = CurlProxyPoolResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        ProxyAgentInfo = JsonConvert.DeserializeObject<ProxyInfo>(ResponseContent);
                    }
                }
                catch (Exception)
                {
                    HttpClientWithProxy = null;
                    return false;
                }

                if (ProxyAgentInfo != null && !string.IsNullOrEmpty(ProxyAgentInfo.proxy))
                {
                    string ProxyAddress = (ProxyAgentInfo.https ? "https" : "http") + "://" + ProxyAgentInfo.proxy;
                    WebProxy WebProxySetting = new WebProxy(ProxyAddress);
                    this.Logger.LogInformation($"Get New Proxy( {ProxyAddress} )");
                    HttpClientHandler HttpClientHandlerWithProxy = new HttpClientHandler
                    {
                        Proxy = WebProxySetting,
                        UseProxy = true,
                        UseCookies = false,
                    };
                    HttpClientWithProxy = new HttpClient(HttpClientHandlerWithProxy);
                    return true;
                }
                HttpClientWithProxy = null;
                return false;
            }
        }

        public HttpRequestMessage UpdateUriRequest(Uri UriRequest)
        {
            HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Get, UriRequest.OriginalString);
            if (this.RequestUserAgentList != null)
            {
                Request.Headers.Add("User-Agent", this.RequestUserAgentList[new Random().Next(this.RequestUserAgentList.Count)]);
            }
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
            HttpRequestMessage Request = this.UpdateUriRequest(UriRequest);
            while (true)
            {
                HttpClient? WebClient;
                if (this.TryGetHttpClientWithProxy(out WebClient))
                {
                    try
                    {
                        HttpResponseMessage Response = WebClient.SendAsync(Request).GetAwaiter().GetResult();
                        if (Response.StatusCode == HttpStatusCode.OK)
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
                        }
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(1);
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        public void AsyncFetchWebContent<T>(Uri UriRequest, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback)
        {
            AsyncWebFetchTask<T> Task = new AsyncWebFetchTask<T>(UriRequest, FinishCallback);
            this.CrawlerTaskList.Enqueue(Task);
            this.Logger.LogInformation($"Add Async Task {UriRequest.OriginalString}");
            this.FlushTaskQueue();
        }

        protected void FlushTaskQueue()
        {
            if (this.CrawlerTaskList.TryDequeue(out AsyncWebFetchTaskBase? Task) && this.TryGetHttpClientWithProxy(out HttpClient? HttpClient))
            {
                this.Logger.LogInformation($" Start Task {Task.URL.OriginalString} With Proxy {HttpClient.DefaultProxy.ToString()}");
                this.DoTask(Task, HttpClient);
            }
        }

        protected void DoTask(AsyncWebFetchTaskBase Task, HttpClient HttpClient)
        {
            HttpRequestMessage Request = this.UpdateUriRequest(Task.URL);
            Task<HttpResponseMessage> TaskResponse = HttpClient.SendAsync(Request);
            this.Logger.LogInformation($"Start HttpClient::SendAsync vist {Task.URL.OriginalString} with Proxy {HttpClient.DefaultProxy.ToString()}");
            TaskResponse.ContinueWith(TaskResponse => this.OnGetAsyncCompleted(TaskResponse, Task, HttpClient));
        }

        protected void OnGetAsyncCompleted(Task<HttpResponseMessage> TaskResponse, AsyncWebFetchTaskBase Task, HttpClient HttpClient)
        {
            if (TaskResponse.IsCompletedSuccessfully)
            {
                this.Logger.LogInformation($"OnGetAsyncCompleted Task ( {Task.URL.OriginalString} ) Is Completed Successfully.");
                if (TaskResponse.Result.IsSuccessStatusCode)
                {
                    this.Logger.LogInformation($"OnGetAsyncCompleted Task ( {Task.URL.OriginalString} ) Is Success Http Status Code.");
                    Task<string> ResponseContentReadStringTask = TaskResponse.Result.Content.ReadAsStringAsync();
                    ResponseContentReadStringTask.ContinueWith(ResponseContentReadStringTask => this.OnResponseContentReadStringCompleted(ResponseContentReadStringTask, Task, HttpClient));
                }
                else
                {
                    this.Logger.LogInformation($"OnGetAsyncCompleted Task ( {Task.URL.OriginalString} ) Is Fail Http Status Code ( {TaskResponse.Result.StatusCode} ).");
                    this.CrawlerTaskList.Enqueue(Task);
                    this.FlushTaskQueue();
                }
            }
            else
            {
                this.Logger.LogInformation($"OnGetAsyncCompleted Task ( {Task.URL.OriginalString} ) Is Fail with Exception ( {TaskResponse.Exception?.ToString()} ).");
                this.CrawlerTaskList.Enqueue(Task);
                this.FlushTaskQueue();
            }
        }

        protected void OnResponseContentReadStringCompleted(Task<string> ResponseContentReadStringTask, AsyncWebFetchTaskBase Task, HttpClient HttpClient)
        {
            if (ResponseContentReadStringTask.IsCompletedSuccessfully)
            {
                this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Is Completed Successfully.");
                if (!string.IsNullOrEmpty(ResponseContentReadStringTask.Result))
                {
                    HtmlDocument WebContent = new HtmlDocument();
                    WebContent.LoadHtml(ResponseContentReadStringTask.Result);
                    List<HtmlParseError> ParseErrors = (List<HtmlParseError>)WebContent.ParseErrors;
                    if (ParseErrors.Count == 0)
                    {
                        this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Html Parse Completed Successfully.");
                        if (WebContent.DocumentNode.InnerText.Length != 0)
                        {
                            this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Html Parse Content Check Length Successfully.");
                            this.CrawlerClientStack.Push(HttpClient);
                            if (this.AsyncWebFetchTaskParseAndCallback(Task, WebContent))
                            {
                                this.Logger.LogInformation($"Task {Task.URL.OriginalString} Completed");
                                this.FlushTaskQueue();
                            }
                        }
                        else
                        {
                            this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Html Parse Content Check Error is Null or Empty.");
                            this.CrawlerTaskList.Enqueue(Task);
                            this.FlushTaskQueue();
                        }
                    }
                    else
                    {
                        this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Html Parse Had Error Flow:");
                        foreach (HtmlParseError ParserError in ParseErrors)
                        {
                            this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Html Parse Had Error : {ParserError.ToString()}");
                        }
                        this.CrawlerTaskList.Enqueue(Task);
                        this.FlushTaskQueue();
                    }
                }
                else
                {
                    this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Failed ResponseContentReadStringTask.Result is Null Or Empty.");
                    this.CrawlerTaskList.Enqueue(Task);
                    this.FlushTaskQueue();
                }
            }
            else
            {
                this.Logger.LogInformation($"OnResponseContentReadStringCompleted Task ( {Task.URL.OriginalString} ) Failed With Excption ( {Task.exception?.ToString()}.");
                this.CrawlerTaskList.Enqueue(Task);
                this.FlushTaskQueue();
            }
        }

        protected bool AsyncWebFetchTaskParseAndCallback(AsyncWebFetchTaskBase Task, HtmlDocument WebContent)
        {
            Task.Result = WebContent;
            Task.StringResult = WebContent.DocumentNode.OuterHtml;
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
                    this.Logger.LogInformation($"AsyncWebFetchTaskParseAndCallback Task {Task.URL.OriginalString} Faild That ParseModelMethod is Null");
                    return false;
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
                    this.Logger.LogInformation($"AsyncWebFetchTaskParseAndCallback Task {Task.URL.OriginalString} Faild That ReturnTypes.Length Must is 1.");
                    return false;
                }
                else
                {
                    TaskCallbackFunctionMethod.Invoke(Task, new object[] { });
                }
            }
            else
            {
                this.Logger.LogInformation($"AsyncWebFetchTaskParseAndCallback Task {Task.URL.OriginalString} Faild That ParseModelMethod is Null");
                return false;
            }
            return true;
        }

        public void OnTickTrigger()
        {
            this.FlushTaskQueue();
        }
    }

    public class CrawlerService : ICrawlerService
    {
        private readonly ILogger<CrawlerService> Logger;
        private readonly IConfigurationSection Configure;
        private readonly IModelParserService ModelParserService;
        private readonly ITimedTriggerService TimeTriggerService;
        private Dictionary<string, DomainCrawlerManager> DomainCrawlerManagers;
        public CrawlerService(ILogger<CrawlerService> Logger, IConfiguration Configuration, IModelParserService ModelParserService, ITimedTriggerService TimedTriggerService)
        {
            this.Logger = Logger;
            this.DomainCrawlerManagers = new Dictionary<string, DomainCrawlerManager>();
            this.Configure = Configuration.GetSection("Services").GetSection("Carwler");
            this.ModelParserService = ModelParserService;
            this.TimeTriggerService = TimedTriggerService;
            this.TimeTriggerService.RegisterTickTrigger(this.OnTickTrigger);
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

        private void OnTickTrigger()
        {
            foreach (DomainCrawlerManager Manager in this.DomainCrawlerManagers.Values)
            {
                Manager.OnTickTrigger();
            }
        }
    }
}