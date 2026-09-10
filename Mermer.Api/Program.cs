using Mermer.Api.Endpoints;
using Mermer.Api.Services;
using Mermer.Data.Postgres;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://*:5050");

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string is not configured. Set 'Postgres' or 'DefaultConnection'.");

builder.Services.AddMermerPostgres(connectionString);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? new[] { "*" };

        if (origins.Length == 1 && origins[0] == "*")
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Mermer ERP API",
        Version = "v1",
        Description =
            "HTTP API over the new PostgreSQL data layer. " +
            "Replaces the legacy Couchbase access for the WPF client.",
        Contact = new OpenApiContact { Name = "Mermer" }
    });

    options.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy =
        System.Text.Json.JsonNamingPolicy.CamelCase;
});

//  РЕГИСТРАЦИЯ СЕРВИСОВ СИНХРОНИЗАЦИИ 
builder.Services.AddScoped<IStockBalanceCalculator, StockBalanceCalculator>();
builder.Services.AddScoped<ISyncService, SyncService>();

var app = builder.Build();

// ЛОГИРОВАНИЕ ВСЕХ ВХОДЯЩИХ HTTP-ЗАПРОСОВ И ИХ СТАТУСОВ
app.Use(async (context, next) =>
{
    var path = context.Request.Path + context.Request.QueryString;
    var method = context.Request.Method;

    await next();

    var status = context.Response.StatusCode;
    var color = status >= 400 ? ConsoleColor.Red : ConsoleColor.Green;
    var prevColor = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine($"[API LOG] {method} {path} -> {status}");
    Console.ForegroundColor = prevColor;
});

app.UseCors();

// Формируем спецификацию строго в формате Swagger 2.0 (понятно любому UI без сбоев версии 3.0.4)
app.UseSwagger(c =>
{
    c.SerializeAsV2 = true;
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
});

app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Mermer ERP API v1");
    o.RoutePrefix = "swagger";
});

app.MapGet("/", () => Results.Redirect("/swagger"))
   .ExcludeFromDescription();

// РЕГИСТРАЦИЯ ЭНДПОИНТОВ
app.MapHealthEndpoints();
app.MapAuthEndpoints(); // ДОБАВЛЕНО: Регистрация эндпоинтов авторизации (/api/auth/login)
app.MapEnterpriseEndpoints();
app.MapFinanceEndpoints();
app.MapExpensesEndpoints();
app.MapStockSlipsEndpoints();
app.MapStocksEndpoints();
app.MapInvoicesEndpoints();
app.MapSpendingEndpoints();
app.MapDepositoriesEndpoints();
app.MapPartnersEndpoints();
app.MapBalancesEndpoints();
app.MapStockBalancesEndpoints();
app.MapStockRevisionsEndpoints();
app.MapSyncEndpoints();
app.MapStockTransfersEndpoints();
app.MapStockOrdersEndpoints();
app.MapAggregatedStockOrdersEndpoints();
app.MapStockNameComposersEndpoints();
app.MapStockAlternativesEndpoints();
app.MapStockTurnoversEndpoints();
app.MapStockRepriceEndpoints();
app.MapAggregatedReportsEndpoints();
app.MapRevenueReportsEndpoints();
app.MapUsersEndpoints();
app.MapRolesEndpoints();
app.MapStockOrderTemplatesEndpoints();
app.MapLicensingEndpoints();


app.Run();