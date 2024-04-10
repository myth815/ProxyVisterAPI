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
        private readonly ICPWenKuModelService CPWenKuModelService;

        public CategoriesController(ILogger<CategoriesController> OutputLogger, ICPWenKuModelService CPWenKuModelService) : base()
        {
            this.Logger = OutputLogger;
            this.CPWenKuModelService = CPWenKuModelService;
        }

        [HttpGet]
        public IActionResult Get(bool force = false)
        {
            List<CategoryModel>? Categories = this.CPWenKuModelService.GetCategories(force);
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
        private readonly ICPWenKuModelService CPWenKuModelService;

        public BookController(ILogger<BookController> OutputLogger, ICPWenKuModelService CPWenKuModelService) : base()
        {
            this.Logger = OutputLogger;
            this.CPWenKuModelService = CPWenKuModelService;
        }

        [HttpGet("{BookID}")]
        public IActionResult GetBook(int BookID)
        {
            BookModel? Resutl = this.CPWenKuModelService.GetBookModel(BookID);
            if (Resutl != null)
            {
                return Ok(Resutl);
            }
            return Forbid();
        }
    }
}
[ApiController]
[Route("api/cpwenku/[controller]")]
public class BookContentController : ControllerBase
{
    private readonly ILogger<BookContentController> Logger;
    private readonly ICPWenKuModelService CPWenKuModelService;

    public BookContentController(ILogger<BookContentController> OutputLogger, ICPWenKuModelService CPWenKuModelService) : base()
    {
        this.Logger = OutputLogger;
        this.CPWenKuModelService = CPWenKuModelService;
    }

    [HttpGet("{BookID}")]
    public IActionResult GetBookContent(int BookID)
    {
        BookContentModel? Resutl = this.CPWenKuModelService.GetBookContentModel(BookID);
        if (Resutl != null)
        {
            return Ok(Resutl);
        }
        return Forbid();
    }
}