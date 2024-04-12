using ProxyVisterAPI.Services;
using ProxyVisterAPI.Services.CPWenKu;
using System.Text;


// 在应用启动时调用
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ICPWenKuModelParseService, CPWenKuModelParseService>();
builder.Services.AddSingleton<IModelParserService, CPWenKuModelParseService>();
builder.Services.AddSingleton<ICrawerService, CrawerService>();
builder.Services.AddSingleton<IJsonLocalStorageService, JsonLocalStorageService>();
builder.Services.AddSingleton<ITextService, TextService>();
builder.Services.AddSingleton<ICPWenKuLocalStrorageService, CPWenKuLocalStrorageService>();
builder.Services.AddSingleton<ICPWenKuModelService, CPWenKuModelService>();

builder.Services.AddHttpClient();
// Add services to the container.
builder.Services.AddControllers();
// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("AllowAll");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.Run();
