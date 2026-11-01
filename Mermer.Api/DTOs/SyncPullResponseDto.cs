using System;
using System.Collections.Generic;

namespace Mermer.Api.DTOs;

public class SyncPullResponseDto
{
    // 1. Организационная структура и валюты
    public List<OfficeSyncDto> Offices { get; set; } = new();
    public List<WarehouseSyncDto> Warehouses { get; set; } = new();
    public List<DepositorySyncDto> Depositories { get; set; } = new();
    public List<CurrencySyncDto> Currencies { get; set; } = new();
    public List<CurrencyRateSyncDto> CurrencyRates { get; set; } = new();
    public List<PartnerSyncDto> Partners { get; set; } = new();

    // 2. Статьи затрат
    public List<ExpenseSyncDto> Expenses { get; set; } = new();

    // 3. Персонал и безопасность
    public List<RoleSyncDto> Roles { get; set; } = new();
    public List<UserSyncDto> Users { get; set; } = new();

    // 4. Каталог товаров, единицы и цены
    public List<StockSyncDto> Stocks { get; set; } = new();
    public List<StockUnitSyncDto> StockUnits { get; set; } = new();
    public List<StockPriceSyncDto> StockPrices { get; set; } = new();
    public List<StockAdditionalPriceSyncDto> StockAdditionalPrices { get; set; } = new();
    public List<StockBalanceSyncDto> StockBalances { get; set; } = new();

    // 5. Аналоги товаров
    public List<StockAlternativeSyncDto> StockAlternatives { get; set; } = new();
    public List<StockAlternativeLineSyncDto> StockAlternativeLines { get; set; } = new();

    public DateTimeOffset ServerTime { get; set; } = DateTimeOffset.UtcNow;
}

public class OfficeSyncDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
    public bool IsDisabled { get; set; }
}

public class WarehouseSyncDto
{
    public Guid Id { get; set; }
    public Guid? OfficeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
    public bool IsDisabled { get; set; }
}

public class DepositorySyncDto
{
    public Guid Id { get; set; }
    public Guid? OfficeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
    public bool IsDisabled { get; set; }
}

public class CurrencySyncDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Decimals { get; set; } = 2;
    public bool IsDefault { get; set; }
    public string? Description { get; set; }
    public bool IsDisabled { get; set; }
}

public class CurrencyRateSyncDto
{
    public Guid Id { get; set; }
    public Guid CurrencyId { get; set; }
    public DateTime ValidFrom { get; set; }
    public decimal Multiplier { get; set; }
    public decimal Divider { get; set; }
}

public class PartnerSyncDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Group { get; set; }
    public decimal? CreditLimit { get; set; }
    public string[]? Tags { get; set; }
    public string? Description { get; set; }
    public decimal Rating { get; set; }
    public Guid? CurrencyId { get; set; }
    public bool IsDisabled { get; set; }
}

public class ExpenseSyncDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Group { get; set; }
    public string[]? Tags { get; set; }
    public bool IsDisabled { get; set; }
}

public class RoleSyncDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AuthorizationsJson { get; set; }
    public string? AccountPrivilegesJson { get; set; }
    public bool IsDisabled { get; set; }
}

public class UserSyncDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public string? Description { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class StockSyncDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? Type { get; set; }
    public string? Group { get; set; }
    public string[]? Tags { get; set; }
    public string[]? Barcodes { get; set; }
    public decimal? LimitMin { get; set; }
    public decimal? LimitMax { get; set; }
    public string? Description { get; set; }
    public bool IsDisabled { get; set; }
}

public class StockUnitSyncDto
{
    public Guid Id { get; set; }
    public Guid StockId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Multiplier { get; set; }
    public decimal Divider { get; set; }
    public bool IsDefault { get; set; }
}

public class StockPriceSyncDto
{
    public Guid Id { get; set; }
    public Guid StockId { get; set; }
    public DateTime ValidFrom { get; set; }
    public decimal Price { get; set; }
    public Guid? CurrencyId { get; set; }
    public string? PriceGroup { get; set; }
}

public class StockAdditionalPriceSyncDto
{
    public Guid Id { get; set; }
    public Guid StockId { get; set; }
    public decimal Price { get; set; }
    public Guid? CurrencyId { get; set; }
    public string? PriceGroup { get; set; }
    public DateTime ValidFrom { get; set; }
}

public class StockBalanceSyncDto
{
    public Guid WarehouseId { get; set; }
    public Guid StockId { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
}

public class StockAlternativeSyncDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDisabled { get; set; }
}

public class StockAlternativeLineSyncDto
{
    public Guid Id { get; set; }
    public Guid StockAlternativeId { get; set; }
    public Guid StockId { get; set; }
}