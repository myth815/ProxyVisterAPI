using ProxyVisterAPI.Models.CPWenku;
using System.Net;

namespace ProxyVisterAPI.Services.CPWenKu
{
    public class CPWenKuLocalStrorageDefine
    {
        public static readonly string BookModelDirectryPath = Directory.GetCurrentDirectory() + @"\CPWenKuContent\Books\";
        public static readonly string BookContentModelDirectryPath = Directory.GetCurrentDirectory() + @"\CPWenKuContent\BookContents\";
        public static readonly string CategoryDirectoryPath = Directory.GetCurrentDirectory() + @"\CPWenKuContent\Category\";
        public static readonly string CategoryModelPath = CategoryModelPath + "Category.json";
    }
    public interface ICPWenKuLocalStrorageService
    {
        BookModel? LoadBookModelFromLocalStrorage(int BookID);
        bool SaveBookModelToLocalStrorage(BookModel Book);

        BookContentModel? LoadBookContentModelFromLocalStrorage(int BookID);
        bool SaveBookContentModelToLocalStrorage(BookContentModel BookContentModel);

        bool SaveCategoryModelToLocalStrorage(List<CategoryModel> CategoryModels);
        List<CategoryModel>? LoadCategoryModelFromLocalStrorage();

        List<BookModel>? LoadBookListWithCategoryFromLocal(string CategoryName);
        bool SaveBookListWithCategoryToLocal(List<BookModel> BookList, string CategoryName);
    }

    public class CPWenKuLocalStrorageService : ICPWenKuLocalStrorageService
    {
        ILogger<CPWenKuLocalStrorageService> Logger;
        IJsonLocalStorageService JsonLocalStorageService;
        public CPWenKuLocalStrorageService(IJsonLocalStorageService Service, ILogger<CPWenKuLocalStrorageService> ServiceLogger)
        {
            this.Logger = ServiceLogger;
            this.JsonLocalStorageService = Service;
        }

        private void CreateDirectoryDependOnFile(string FilePath)
        {
            FileInfo FileInfo = new FileInfo(FilePath);
            if (FileInfo.Directory != null && FileInfo.Directory.Exists == false)
            {
                FileInfo.Directory.Create();
            }
        }

        public BookModel? LoadBookModelFromLocalStrorage(int BookID)
        {
            string BookModelPath = CPWenKuLocalStrorageDefine.BookModelDirectryPath + BookID.ToString() + ".json";
            if(File.Exists(BookModelPath))
            {
                return this.JsonLocalStorageService.LoadFromLocalStroage<BookModel>(BookModelPath);
            }
            return null;
        }
        public bool SaveBookModelToLocalStrorage(BookModel Book)
        {
            string BookModelPath = CPWenKuLocalStrorageDefine.BookModelDirectryPath + Book.ID.ToString() + ".json";
            this.CreateDirectoryDependOnFile(BookModelPath);
            return this.JsonLocalStorageService.SaveToLocalStrorage(Book, BookModelPath);
        }

        public BookContentModel? LoadBookContentModelFromLocalStrorage(int BookID)
        {
            string FilePath = CPWenKuLocalStrorageDefine.BookContentModelDirectryPath + BookID.ToString() + ".json";
            if (File.Exists(FilePath))
            {
                return this.JsonLocalStorageService.LoadFromLocalStroage<BookContentModel>(FilePath);
            }
            return new BookContentModel();
        }

        public bool SaveBookContentModelToLocalStrorage(BookContentModel BookContentModel)
        {
            if(BookContentModel.BookModel != null)
            {
                string FilePath = CPWenKuLocalStrorageDefine.BookContentModelDirectryPath + BookContentModel.BookModel.ID + ".json";
                this.CreateDirectoryDependOnFile(FilePath);
                return this.JsonLocalStorageService.SaveToLocalStrorage(BookContentModel, FilePath);
            }
            return false;
        }

        public bool SaveCategoryModelToLocalStrorage(List<CategoryModel> CategoryModels)
        {
            string FilePath = CPWenKuLocalStrorageDefine.CategoryModelPath;
            this.CreateDirectoryDependOnFile(FilePath);
            return this.JsonLocalStorageService.SaveToLocalStrorage(CategoryModels, CPWenKuLocalStrorageDefine.CategoryModelPath);
        }

        public List<CategoryModel>? LoadCategoryModelFromLocalStrorage()
        {
            return this.JsonLocalStorageService.LoadFromLocalStroage<List<CategoryModel>>(CPWenKuLocalStrorageDefine.CategoryModelPath);
        }

        public List<BookModel>? LoadBookListWithCategoryFromLocal(string CategoryName)
        {
            string FilePath = CPWenKuLocalStrorageDefine.CategoryDirectoryPath + CategoryName + ".json";
            if (File.Exists(FilePath))
            {
                return this.JsonLocalStorageService.LoadFromLocalStroage<List<BookModel>>(FilePath);
            }
            return null;
        }

        public bool SaveBookListWithCategoryToLocal(List<BookModel> BookList, string CategoryName)
        {
            string FilePath = CPWenKuLocalStrorageDefine.CategoryDirectoryPath + CategoryName + ".json";
            this.CreateDirectoryDependOnFile(FilePath);
            return this.JsonLocalStorageService.SaveToLocalStrorage(BookList, FilePath);
        }
    }
}