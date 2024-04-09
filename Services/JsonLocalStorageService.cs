using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ProxyVisterAPI.Services
{
    public interface IJsonLocalStorageService
    {
        bool SaveToLocalStrorage(object Object, string Path);
        T? LoadFromLocalStroage<T>(string LocalStroagePath);
    }
    public class JsonLocalStorageService : IJsonLocalStorageService
    {
        private Logger<JsonLocalStorageService> Logger;
        public JsonLocalStorageService(Logger<JsonLocalStorageService> ServiceLogger)
        {
            Logger = ServiceLogger;
        }
        public bool SaveToLocalStrorage(object Object, string Path)
        {
            string FileContent = string.Empty;
            try
            {
                FileContent = JsonConvert.SerializeObject(Object);
            }
            catch(Exception ex)
            {
                this.Logger.LogError($"SaveToLocalStrorage : JsonConvert.SerializeObject Exception( {ex.Message} )", ex);
                return false;
            }
            try
            {
                File.WriteAllText(Path, FileContent);
                return true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError($"SaveToLocalStrorage : File.WriteAllText to File Path ( {Path} ) With Exception( {ex.Message} )", ex);
                return false;
            }
        }

        public T? LoadFromLocalStroage<T>(string LocalStroagePath)
        {
            if(File.Exists(LocalStroagePath))
            {
                string FileContent = File.ReadAllText(LocalStroagePath);
                try
                {
                    T? Object = JsonConvert.DeserializeObject<T>(FileContent);
                    return Object;
                }
                catch(Exception ex)
                {
                    this.Logger.LogError($"LoadFromLocalStroage : JsonConvert.DeserializeObject Exception( {ex.Message} )", ex);
                    return default;
                }
            }
            else
            {
                this.Logger.LogError($"LoadFromLocalStroage :: File Path ( {LocalStroagePath} ) Not Exsit!");
                return default;
            }
        }
    }
}