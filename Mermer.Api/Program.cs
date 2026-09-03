using Mermer.Api.Endpoints;
using Mermer.Api.Services;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Connection string 'Postgres' is not configured. " +
        "Set it in appsettings.json or via environment variable " +
        "ConnectionStrings__Postgres.");

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

app.UseCors();

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Mermer Binyat API v1");
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