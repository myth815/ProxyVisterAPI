using ProxyVisterAPI.Models.CPWenku;

namespace ProxyVisterAPI.Services.CPWenKu
{
    public interface ICPWenKuLocalStrorageService
    {
        BookModel? LoadBookModelFromLocalStrorage(int BookID);
        bool SaveBookModelToLocalStrorage(BookModel Book);
    }

    public class CPWenKuLocalStrorageService : ICPWenKuLocalStrorageService
    {
        Logger<CPWenKuLocalStrorageService> Logger;
        IJsonLocalStorageService JsonLocalStorageService;
        public CPWenKuLocalStrorageService(Logger<CPWenKuLocalStrorageService> ServiceLogger, IJsonLocalStorageService Service)
        {
            this.Logger = ServiceLogger;
            this.JsonLocalStorageService = Service;
        }

        public BookModel? LoadBookModelFromLocalStrorage(int BookID)
        {
            string BookModelPath = "";
            return this.JsonLocalStorageService.LoadFromLocalStroage<BookModel>(BookModelPath);
        }
        public bool SaveBookModelToLocalStrorage(BookModel Book)
        {
            string BookModelPath = "";
            return this.JsonLocalStorageService.SaveToLocalStrorage(Book, BookModelPath);
        }
    }
}