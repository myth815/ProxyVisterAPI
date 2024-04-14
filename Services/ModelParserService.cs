using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProxyVisterAPI.Models.CPWenku;
using System.Reflection;

namespace ProxyVisterAPI.Services
{
    public class ModelBase
    {
        public string? Url { get; set; }
    }

    public interface IModelParserService
    {
        T? ParseModel<T>(HtmlDocument Document);
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)] // 限制此属性只能用于方法
    public class SpecialAttribute : Attribute
    {
        public string Description { get; }

        public SpecialAttribute(string description)
        {
            Description = description;
        }
    }

    public class ModelParserService : IModelParserService
    {
        protected ILogger<ModelParserService> Logger;
        protected Dictionary<Type, MethodInfo> ModelParsers;
        public ModelParserService(ILogger<ModelParserService> Logger)
        {
            this.Logger = Logger;
            
            this.ModelParsers = new Dictionary<Type, MethodInfo>();
            MethodInfo[] Methods = this.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (MethodInfo Method in Methods)
            {
                var Attribute = Method.GetCustomAttribute<SpecialAttribute>();
                if (Attribute != null)
                {
                    Type ReturnType = Method.ReturnType;
                    ParameterInfo[] ParameterInfos = Method.GetParameters();
                    // 要求输入参数必须为HtmlDocument
                    if(!(ParameterInfos.Length == 1 && ParameterInfos[0].ParameterType == typeof(HtmlDocument)))
                    {
                        throw new InvalidOperationException();
                    }
                    if (ReturnType.IsGenericType)
                    {
                        Type[] GenericArguments = ReturnType.GetGenericArguments();
                        if (GenericArguments.Length == 1)
                        {
                            Type ChildType = GenericArguments[0];
                            if(!typeof(ModelBase).IsAssignableFrom(ChildType))
                            {
                                throw new InvalidOperationException();
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    else
                    {
                        if(!typeof(ModelBase).IsAssignableFrom(ReturnType))
                        {
                            throw new InvalidOperationException();
                        }
                    }
                    if(this.ModelParsers.ContainsKey(ReturnType))
                    {
                        this.Logger.LogError($"ModelParserService: ModelParser for {ReturnType.Name} already exists, skip {Method.Name}");
                    }
                    else
                    {
                        this.ModelParsers.Add(ReturnType, Method);
                    }
                }
            }
        }

        public T? ParseModel<T>(HtmlDocument Document)
        {
            if(this.ModelParsers.ContainsKey(typeof(T)))
            {
                object? Result = this.ModelParsers[typeof(T)].Invoke(this, new object[] { Document });
                if(Result != null)
                {
                    return (T)Result;
                }
                else
                {
                    this.Logger.LogError($"ModelParserService: ModelParser for {typeof(T).Name} return null.");
                    return default;
                }
            }
            return default;
        }
    }
}
