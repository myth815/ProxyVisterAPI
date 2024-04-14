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

        public void AsyncFetchWebContent<T>(Uri UriRequest, AsyncWebFetchTask<T>.TaskCompletedCallback FinishCallback)
        {
            AsyncWebFetchTask<T> Task = new AsyncWebFetchTask<T>(UriRequest, FinishCallback);
            this.CrawerTaskList.Enqueue(Task);
            this.Logger.LogInformation($"Add Async Task {UriRequest.OriginalString}");
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
            if (this.HttpClientPool.TryPop(out HttpClient? Client))
            {
                if(!this.CrawerTaskList.TryDequeue(out AsyncWebFetchTaskBase? Task))
                {
                    this.HttpClientPool.Push(Client);
                    return;
                }
                Task<HttpResponseMessage> TaskResponse = Client.GetAsync(Task.URL);
                TaskResponse.ContinueWith(TaskResponse =>
                {
                    HttpResponseMessage Response = TaskResponse.Result;
                    switch (Response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            {
                                HtmlDocument WebContent = new HtmlDocument();
                                Task<string> TaskResponseContent = Response.Content.ReadAsStringAsync();
                                TaskResponseContent.ContinueWith(TaskResponseContent =>
                                {
                                    WebContent.LoadHtml(TaskResponseContent.Result);
                                    if(WebContent.DocumentNode.InnerText.Length == 0)
                                    {
                                        Task.RryCount++;
                                        if (Task.RryCount < MaxTryCount)
                                        {
                                            this.Logger.LogError($"ParseModel Fail with URL({Task.URL}), Retry");
                                            Thread.Sleep(this.VistIntervals);
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
                                    Task.Result = WebContent;
                                    Task.StringResult = TaskResponseContent.Result;
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
                                },
                                TaskContinuationOptions.OnlyOnRanToCompletion);
                                break;
                            }
                        case HttpStatusCode.Forbidden :
                            {
                                Thread.Sleep(this.VistIntervals);
                                Task.RryCount++;
                                if (Task.RryCount < MaxTryCount)
                                {
                                    this.CrawerTaskList.Enqueue(Task);
                                }
                                this.HttpClientPool.Push(this.GetHttpClientWithProxy());
                                return;
                            }
                        default:
                            {
                                Task.RryCount++;
                                if (Task.RryCount < MaxTryCount)
                                {
                                    this.CrawerTaskList.Enqueue(Task);
                                }
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
                this.WebProxySettings = new WebProxy(ProxyUri);
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
            if (Manager == null)
            {
                throw new Exception("Can't Get DomainName From RequestURL( " + RequestURL + " )");
            }
            Manager.AsyncFetchWebContent(UriRequest, FinishCallback);
        }
    }
}