using Microsoft.AspNetCore.Mvc;
using ProxyVisterAPI.Models.CPWenku;
using ProxyVisterAPI.Services.CPWenKu;

namespace ProxyVisterAPI.Controllers.CPWenKu
{
    public class CPWenKuJsonRESTFulController
    {
    }

    [Route("api/cpwenku/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly ILogger<CategoriesController> Logger;
        private readonly ICPWenKuCrawerService CPWenKuCrawerServiceInstance;

        public CategoriesController(ILogger<CategoriesController> OutputLogger, ICPWenKuCrawerService CPWenKuCrawerServiceInstance) : base()
        {
            this.Logger = OutputLogger;
            this.CPWenKuCrawerServiceInstance = CPWenKuCrawerServiceInstance;
        }

        [HttpGet]
        public IActionResult Get(bool force = false)
        {
            List<CategoryModel>? Categories = this.CPWenKuCrawerServiceInstance.GetCategories(force);
            if (Categories != null)
            {
                return Ok(Categories);
            }
            return Forbid();
        }
    }

    [ApiController]
    [Route("api/cpwenku/[controller]")]
    public class BookController : ControllerBase
    {
        private readonly ILogger<BookController> Logger;
        private readonly ICPWenKuCrawerService CPWenKuCrawerServiceInstance;

        public BookController(ILogger<BookController> OutputLogger, ICPWenKuCrawerService CPWenKuCrawerServiceInstance) : base()
        {
            this.Logger = OutputLogger;
            this.CPWenKuCrawerServiceInstance = CPWenKuCrawerServiceInstance;
        }

        [HttpGet("{BookID}")]
        public IActionResult GetBook(int BookID, bool force = false)
        {
            BookModel? Resutl = this.CPWenKuCrawerServiceInstance.GetBookModel(BookID);
            if (Resutl != null)
            {
                return Ok(Resutl);
            }
            return Forbid();
        }
    }
}
