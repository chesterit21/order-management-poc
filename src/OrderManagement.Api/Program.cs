using Microsoft.OpenApi;
using OrderManagement.Api.Extensions;
using OrderManagement.Api.Options;
using OrderManagement.Api.Swagger;
using OrderManagement.Application;
using OrderManagement.Infrastructure;
using OrderManagement.Infrastructure.Files;
using OrderManagement.Infrastructure.Options;
using OrderManagement.Application.Abstractions.Files;
using Microsoft.Extensions.Options;
using Serilog;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order Management API",
        Version = "v1"
    });

    // Enable Swagger annotations
    options.EnableAnnotations();

    // Handle IFormFile properly for file uploads
    options.UseInlineDefinitionsForEnums();
    options.ResolveConflictingActions(apiDesc => apiDesc.First());
    options.OperationFilter<FileUploadOperationFilter>();

    // Include XML comments if available
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.Configure<ClientCorsOptions>(
    builder.Configuration.GetSection(ClientCorsOptions.SectionName));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 注册IFileStorageService
builder.Services.AddScoped<IFileStorageService>(sp =>
{
    var options = sp.GetRequiredService<IOptions<FileUploadOptions>>();
    var environment = sp.GetRequiredService<IWebHostEnvironment>();
    var rootPath = Path.Combine(environment.WebRootPath, options.Value.ProductImageRootPath);
    return new LocalProductImageStorageService(options, rootPath);
});

builder.Services.AddApiServices();

builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiAuthorization();

builder.Services.AddCors(options =>
{
    var corsOptions = builder.Configuration
        .GetSection(ClientCorsOptions.SectionName)
        .Get<ClientCorsOptions>() ?? new ClientCorsOptions();

    options.AddPolicy("ClientApps", policy =>
    {
        var origins = corsOptions.AllowedOrigins.Length > 0
            ? corsOptions.AllowedOrigins
            : ["http://localhost:3000"];

        policy
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-ID")
            .WithExposedHeaders("X-Correlation-ID", "Location");
    });
});

builder.Services.AddHealthChecks();

// Register Activity Log Context Accessor
builder.Services.AddScoped<OrderManagement.Application.Abstractions.ActivityLogs.IActivityLogContextAccessor, OrderManagement.Api.Extensions.ActivityLogs.HttpActivityLogContextAccessor>();

var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();

app.UseApiMiddlewares();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Management API v1");
});

// NOTE: app.UseSerilogRequestLogging() was removed to avoid duplicate "request completed"
// log entries. The custom RequestLoggingMiddleware (registered via UseApiMiddlewares above)
// already logs request start/completion with correlation ID, user context, and writes the
// RequestCompleted activity log entry to the database. Keeping both would produce two
// separate log lines per request with potentially different status codes for failed requests.

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseCors("ClientApps");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;