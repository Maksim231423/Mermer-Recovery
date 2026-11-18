using Mermer.Api.Endpoints;
using Mermer.Api.Middleware;
using Mermer.Api.Services;
using Mermer.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Npgsql;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

// 1. Включаем legacy timestamp в Npgsql и привязываем процесс к UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
Environment.SetEnvironmentVariable("TZ", "UTC");

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.Warning);

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
        Description = "HTTP API over PostgreSQL data layer.",
        Contact = new OpenApiContact { Name = "Mermer" }
    });

    options.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
});

// Глобальная настройка сериализации JSON
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.Converters.Add(new UniversalDateTimeConverter());
    o.SerializerOptions.Converters.Add(new UniversalDateTimeOffsetConverter());
});

// РЕГИСТРАЦИЯ СЕРВИСОВ СИНХРОНИЗАЦИИ
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.AddHttpClient("VdsSyncClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddScoped<IStockBalanceCalculator, StockBalanceCalculator>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddHostedService<SyncBackgroundWorker>();

// РЕГИСТРАЦИЯ СЕРВИСОВ ЛИЦЕНЗИРОВАНИЯ И КЭША
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IServerLicenseValidationService, ServerLicenseValidationService>();
builder.Services.AddSingleton<ILicenseCryptoService, LicenseCryptoService>();

var app = builder.Build();

// ЛОГИРОВАНИЕ ВСЕХ ВХОДЯЩИХ HTTP-ЗАПРОСОВ
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

// БЕЗОПАСНАЯ ИНИЦИАЛИЗАЦИЯ БД
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MermerDbContext>();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[DB] Проверка схемы базы данных...");

    try
    {
        // Проверяем, существует ли уже физическая таблица в базе данных
        var conn = dbContext.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'currencies');";
        bool tablesExist = (bool)(await cmd.ExecuteScalarAsync() ?? false);

        if (!tablesExist)
        {
            // База абсолютно чистая — накатываем все миграции с нуля
            await dbContext.Database.MigrateAsync();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[DB] Миграции успешно применены!");
        }
        else
        {
            // Схема уже развернута (из дампа или патчера).
            // Синхронизируем таблицу истории миграций без повторного создания таблиц:
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            foreach (var migration in pendingMigrations)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") 
                      VALUES ({0}, '8.0.0') ON CONFLICT DO NOTHING;", migration);
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[DB] Схема базы уже существует и синхронизирована. Продолжаем запуск...");
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[DB] Предупреждение при инициализации базы: {ex.Message}. Продолжаем запуск...");
    }
    finally
    {
        Console.ResetColor();
    }

    // ВЫЗЫВАЕТСЯ ВСЕГДА, НЕЗАВИСИМО ОТ РЕЗУЛЬТАТА МИГРАЦИЙ:
    try
    {
        await MermerDbContext.EnsureClientTenantMigratedAsync(dbContext);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[DB] Активный тенант успешно установлен: '{MermerDbContext.CurrentClientId}'");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] Ошибка привязки тенанта: {ex.Message}");
    }
}

app.UseCors();
app.UseMiddleware<ServerLicenseMiddleware>();

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

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// РЕГИСТРАЦИЯ ЭНДПОИНТОВ
app.MapHealthEndpoints();
app.MapAuthEndpoints();
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
app.MapReportEndpoints();
app.MapOfficeEndpoints();
app.MapWarehouseEndpoints();

app.Run();

/// <summary>
/// Конвертер DateTime: отдает строку без сдвигов и смещений
/// </summary>
public class UniversalDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TryGetDateTime(out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        return DateTime.UtcNow;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss"));
    }
}

/// <summary>
/// Конвертер DateTimeOffset: отдает точное время без локальных искажений
/// </summary>
public class UniversalDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TryGetDateTime(out var dt))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
        }
        if (reader.TryGetDateTimeOffset(out var dto))
        {
            // Сохраняем исходные часы и минуты, обнуляя смещение
            return new DateTimeOffset(dto.DateTime, TimeSpan.Zero);
        }
        return DateTimeOffset.UtcNow;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss"));
    }
}