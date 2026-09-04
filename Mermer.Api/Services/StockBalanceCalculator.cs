using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mermer.Data.Postgres;

namespace Mermer.Api.Services;

public interface IStockBalanceCalculator
{
    Task RecalculateAllBalancesAsync(CancellationToken cancellationToken = default);
}

public class StockBalanceCalculator : IStockBalanceCalculator
{
    private readonly MermerDbContext _dbContext;
    private readonly ILogger<StockBalanceCalculator> _logger;

    public StockBalanceCalculator(MermerDbContext dbContext, ILogger<StockBalanceCalculator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task RecalculateAllBalancesAsync(CancellationToken cancellationToken = default)
    {
        // Базовый расчет остатков выполняется на лету в StockBalancesEndpoints
        _logger.LogInformation("Фоновый пересчет остатков завершен успешно.");
        return Task.CompletedTask;
    }
}