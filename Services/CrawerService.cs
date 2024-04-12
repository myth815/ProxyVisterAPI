using HtmlAgilityPack;
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
        
        public TaskCompletedCallback TaskCallback { get; set; }

        public AsyncWebFetchTask(Uri UriRequest, TaskCompletedCallback TaskCallback):base(UriRequest)
        {
            this.TaskCallback = TaskCallback;
        }
    }

    public class DomainCrawerManager
    {
        protected ILogger<CrawerService> Logger;
        protected IConfigurationSection Configuration;
        IModelParserService ModelParserService;
        protected string DomainName;
        protected WebProxy? WebProxySettings;
        protected int VistIntervals;
        protected uint MaxConnectPool;
        protected uint NumberOfThreads;
        protected uint MaxTryCount;
        protected uint TimeOut;

        protected ConcurrentQueue<AsyncWebFetchTaskBase> CrawerTaskList;
        public DomainCrawerManager(ILogger<CrawerService> Logger, IModelParserService ModelParserService, string DomainName, IConfigurationSection Configuration)
        {
            this.Logger = Logger;
            this.ModelParserService = ModelParserService;
            this.DomainName = DomainName;
            this.Configuration = Configuration;
            this.VistIntervals = this.Configuration.GetValue<int>("VistIntervals");
            this.MaxConnectPool = this.Configuration.GetValue<uint>("MaxConnectPool");
            this.NumberOfThreads = this.Configuration.GetValue<uint>("NumberOfThreads");
            this.MaxTryCount = this.Configuration.GetValue<uint>("MaxTryCount");
            this.TimeOut = this.Configuration.GetValue<uint>("TimeOut");
            this.SetupProxy();
            this.CrawerTaskList = new ConcurrentQueue<AsyncWebFetchTaskBase>();
        }

        public void AsyncGetWebContent<T>(Uri UriRequest, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback)
        {
            AsyncWebFetchTask<T> Task = new AsyncWebFetchTask<T>(UriRequest, FinishCallback);
            this.CrawerTaskList.Enqueue(Task);
            this.FlushTaskQueue();
        }

        public T? FetchWebContent<T>(Uri UriRequest)
        {
            while (true)
            {
                HttpClient WebClient = GetHttpClientWithProxy();
                HttpResponseMessage Response = WebClient.GetAsync(UriRequest).GetAwaiter().GetResult();
                switch (Response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        {
                            HtmlDocument WebContent = new HtmlDocument();
                            string ResponseContent = Response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            WebContent.LoadHtml(ResponseContent);
                            if(typeof(T) == typeof(HtmlDocument))
                            {
                                T Result = (T)Convert.ChangeType(WebContent, typeof(T));
                                return Result;
                            }
                            else
                            {
                                return this.ModelParserService.ParseModel<T>(WebContent);
                            }
                        }
                    default:
                        {
                            Thread.Sleep(VistIntervals);
                            continue;
                        }
                }
            }
        }

        protected void FlushTaskQueue()
        {
            if (this.CrawerTaskList.Count > 0)
            {
                for (int i = 0; i < this.NumberOfThreads; i++)
                {
                    if (this.CrawerTaskList.TryDequeue(out AsyncWebFetchTaskBase? Task))
                    {
                        Task.Result = this.FetchWebContent<HtmlDocument>(Task.URL);
                        Type Tasktype = typeof(Task);
                        if(Task != null && Tasktype != null)
                        {
                            MethodInfo? CallbackMethod = Tasktype.GetMethod("TaskCallback");
                            if(CallbackMethod != null)
                            {
                                CallbackMethod.Invoke(Task, [Task]);
                            }
                        }
                    }
                }
            }
        }

        protected HttpClient GetHttpClientWithProxy()
        {
            HttpClientHandler httpClientHandler = new HttpClientHandler
            {
                Proxy = this.WebProxySettings,
                UseProxy = this.WebProxySettings != null,
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
                this.WebProxySettings = new WebProxy($"{ProxyProtol}://{ProxyHost}:{ProxyPort}");
                string? ProxyUserName = ProxySection.GetValue<string>("UserName");
                string? ProxyPassword = ProxySection.GetValue<string>("Password");
                if (!string.IsNullOrEmpty(ProxyUserName))
                {
                    this.WebProxySettings.Credentials = new NetworkCredential(ProxyUserName, ProxyPassword);
                }
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
            if (Manager != null)
            {
                Manager.AsyncGetWebContent(UriRequest, FinishCallback);
            }
            throw new Exception("Can't Get DomainName From RequestURL( " + RequestURL + " )");
        }
    }
}