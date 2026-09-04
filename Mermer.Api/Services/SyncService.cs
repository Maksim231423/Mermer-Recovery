using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mermer.Api.DTOs;
using Mermer.Data.Postgres;
using Mermer.Data.Postgres.Entities;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Api.Services;

public class SyncService : ISyncService
{
    private readonly MermerDbContext _dbContext;
    private readonly IStockBalanceCalculator _balanceCalculator;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        MermerDbContext dbContext,
        IStockBalanceCalculator balanceCalculator,
        ILogger<SyncService> logger)
    {
        _dbContext = dbContext;
        _balanceCalculator = balanceCalculator;
        _logger = logger;
    }

    public async Task<SyncPullResponseDto> ProcessPullAsync(CancellationToken cancellationToken = default)
    {
        var response = new SyncPullResponseDto
        {
            ServerTime = DateTimeOffset.UtcNow
        };

        // 1. Получаем список номенклатуры / товаров
        response.Stocks = await _dbContext.Stocks
            .AsNoTracking()
            .Where(s => !s.IsDisabled)
            .Select(s => new StockDto
            {
                Id = s.Id,
                Code = s.Code ?? string.Empty,
                Title = s.Name ?? string.Empty, // В БД поле называется Name
                Unit = null,                    // В StockEntity нет поля Unit
                Price = 0                       // В StockEntity нет скалярного Price (используются коллекции)
            })
            .ToListAsync(cancellationToken);

        // 2. Получаем список складов
        response.Warehouses = await _dbContext.Warehouses
            .AsNoTracking()
            .Where(w => !w.IsDisabled)
            .Select(w => new WarehouseDto
            {
                Id = w.Id,
                Code = string.Empty,            // В WarehouseEntity нет поля Code
                Title = w.Name ?? string.Empty  // В БД поле называется Name
            })
            .ToListAsync(cancellationToken);

        // 3. Получаем список контрагентов / партнеров
        response.Partners = await _dbContext.Partners
            .AsNoTracking()
            .Where(p => !p.IsDisabled)
            .Select(p => new PartnerDto
            {
                Id = p.Id,
                Code = p.Code ?? string.Empty,
                Title = p.Name ?? string.Empty, // В БД поле называется Name
                Phone = p.Phone,
                Email = null                    // В PartnerEntity нет поля Email
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Подготовлен пакет справочников для Pull: {Stocks} товаров, {Warehouses} складов, {Partners} партнеров",
            response.Stocks.Count, response.Warehouses.Count, response.Partners.Count);

        return response;
    }

    public async Task<SyncPushResponseDto> ProcessPushAsync(SyncPushRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = new SyncPushResponseDto();

        if (request == null)
        {
            response.Success = false;
            response.Errors.Add("Пустой запрос синхронизации.");
            return response;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;

                    // Обработка Invoices
                    foreach (var invDto in request.Invoices)
                    {
                        var exists = await _dbContext.Invoices.AnyAsync(i => i.Id == invDto.Id, cancellationToken);
                        if (exists) continue;

                        var invoice = new InvoiceEntity
                        {
                            Id = invDto.Id,
                            Code = invDto.InvoiceNumber,
                            InvoiceType = invDto.InvoiceType,
                            Date = invDto.Date,
                            UserId = invDto.ClientId,
                            Description = invDto.Description,
                            IsCompleted = true,
                            IsDisabled = false,
                            CreatedAt = now,
                            UpdatedAt = now
                        };

                        foreach (var lineDto in invDto.Lines)
                        {
                            invoice.Lines.Add(new InvoiceLineEntity
                            {
                                Id = lineDto.Id == Guid.Empty ? Guid.NewGuid() : lineDto.Id,
                                InvoiceId = invoice.Id,
                                StockId = lineDto.ProductId,
                                Quantity = lineDto.Quantity,
                                Price = lineDto.UnitPrice,
                                SortOrder = invDto.Lines.IndexOf(lineDto)
                            });
                        }

                        _dbContext.Invoices.Add(invoice);
                        response.ProcessedInvoicesCount++;
                    }

                    // Обработка StockSlips
                    foreach (var slipDto in request.StockSlips)
                    {
                        var exists = await _dbContext.StockSlips.AnyAsync(s => s.Id == slipDto.Id, cancellationToken);
                        if (exists) continue;

                        var slip = new StockSlipEntity
                        {
                            Id = slipDto.Id,
                            Code = slipDto.SlipNumber,
                            SlipType = slipDto.SlipType,
                            Date = slipDto.Date,
                            WarehouseId = slipDto.StockId,
                            Description = slipDto.Description,
                            IsCompleted = true,
                            IsStockIncome = slipDto.SlipType.Equals("Incoming", StringComparison.OrdinalIgnoreCase),
                            CreatedAt = now,
                            UpdatedAt = now
                        };

                        foreach (var lineDto in slipDto.Lines)
                        {
                            slip.Lines.Add(new StockSlipLineEntity
                            {
                                Id = lineDto.Id == Guid.Empty ? Guid.NewGuid() : lineDto.Id,
                                StockSlipId = slip.Id,
                                StockId = lineDto.ProductId,
                                Quantity = lineDto.Quantity,
                                SortOrder = slipDto.Lines.IndexOf(lineDto)
                            });
                        }

                        _dbContext.StockSlips.Add(slip);
                        response.ProcessedStockSlipsCount++;
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Ошибка при сохранении документов от клиента {ClientId}", request.ClientId);

                    response.Success = false;
                    response.Errors.Clear();
                    var detailedError = ex.InnerException?.Message ?? ex.Message;
                    response.Errors.Add($"Ошибка записи документов: {detailedError}");
                    return response;
                }
            }

            try
            {
                await _balanceCalculator.RecalculateAllBalancesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Документы сохранены, но возникла ошибка при пересчете остатков для клиента {ClientId}", request.ClientId);
                response.Errors.Add($"Предупреждение: остатки не обновлены: {ex.Message}");
            }

            response.Success = true;
            _logger.LogInformation("Успешно синхронизировано {Invoices} накладных и {Slips} актов от клиента {ClientId}",
                response.ProcessedInvoicesCount, response.ProcessedStockSlipsCount, request.ClientId);

            return response;
        });
    }
}