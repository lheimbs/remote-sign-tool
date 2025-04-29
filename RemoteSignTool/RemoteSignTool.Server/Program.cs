using Microsoft.AspNetCore.Mvc;
using RemoteSignTool.Server.Services;
using NLog.Web;

/// <summary>
/// Main entry point for the RemoteSignTool.Server application.
/// </summary>
var builder = WebApplication.CreateBuilder(args);

// Configure NLog
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSingleton<ISignToolService, SignToolService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

app.Run();
