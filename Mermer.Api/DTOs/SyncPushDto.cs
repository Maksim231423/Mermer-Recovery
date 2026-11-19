using System;
using System.Collections.Generic;

namespace Mermer.Api.DTOs;

// =========================================================================
// 1. PUSH ЗАПРОС И ОТВЕТ
// =========================================================================

public class SyncPushRequestDto
{
    public string ClientId { get; set; } = string.Empty;

    // Торговля и склад
    public List<InvoiceSyncDto> Invoices { get; set; } = new();
    public List<StockSlipSyncDto> StockSlips { get; set; } = new();
    public List<StockTransferSyncDto> StockTransfers { get; set; } = new();
    public List<StockRevisionSyncDto> StockRevisions { get; set; } = new();
    public List<StockOrderSyncDto> StockOrders { get; set; } = new();
    public List<AggregatedStockOrderSyncDto> AggregatedStockOrders { get; set; } = new();

    // Финансы и касса
    public List<FundsSlipSyncDto> FundsSlips { get; set; } = new();
    public List<FundsTransferSyncDto> FundsTransfers { get; set; } = new();
    public List<DailyFundsRegisterySyncDto> DailyFundsRegisteries { get; set; } = new();
    public List<ExpenseSlipSyncDto> ExpenseSlips { get; set; } = new();

    // Взаиморасчеты с контрагентами
    public List<PartnerSlipSyncDto> PartnerSlips { get; set; } = new();
    public List<PartnerTransferSyncDto> PartnerTransfers { get; set; } = new();
    public List<PartnerSyncDto> Partners { get; set; } = new();

    // Пользователи и роли
    public List<UserSyncDto> Users { get; set; } = new();
    public List<RoleSyncDto> Roles { get; set; } = new();
}

public class SyncPushResponseDto
{
    public bool Success { get; set; }
    public List<string> Errors { get; set; } = new();

    public int ProcessedInvoicesCount { get; set; }
    public int ProcessedStockSlipsCount { get; set; }
    public int ProcessedStockTransfersCount { get; set; }
    public int ProcessedStockRevisionsCount { get; set; }
    public int ProcessedStockOrdersCount { get; set; }
    public int ProcessedAggregatedStockOrdersCount { get; set; }
    public int ProcessedFundsSlipsCount { get; set; }
    public int ProcessedFundsTransfersCount { get; set; }
    public int ProcessedDailyFundsRegisteriesCount { get; set; }
    public int ProcessedExpenseSlipsCount { get; set; }
    public int ProcessedPartnerSlipsCount { get; set; }
    public int ProcessedPartnerTransfersCount { get; set; }
    public int ProcessedPartnersCount { get; set; }
    public int ProcessedUsersCount { get; set; }
    public int ProcessedRolesCount { get; set; }
}

// =========================================================================
// 2. PULL ОТВЕТ
// =========================================================================

public class SyncPullResponseDto
{
    public List<OfficeSyncDto> Offices { get; set; } = new();
    public List<WarehouseSyncDto> Warehouses { get; set; } = new();
    public List<DepositorySyncDto> Depositories { get; set; } = new();
    public List<CurrencySyncDto> Currencies { get; set; } = new();
    public List<CurrencyRateSyncDto> CurrencyRates { get; set; } = new();
    public List<PartnerSyncDto> Partners { get; set; } = new();
    public List<ExpenseSyncDto> Expenses { get; set; } = new();
    public List<RoleSyncDto> Roles { get; set; } = new();
    public List<UserSyncDto> Users { get; set; } = new();

    public List<StockSyncDto> Stocks { get; set; } = new();
    public List<StockUnitSyncDto> StockUnits { get; set; } = new();
    public List<StockPriceSyncDto> StockPrices { get; set; } = new();
    public List<StockAdditionalPriceSyncDto> StockAdditionalPrices { get; set; } = new();
    public List<StockBalanceSyncDto> StockBalances { get; set; } = new();

    public List<StockAlternativeSyncDto> StockAlternatives { get; set; } = new();
    public List<StockAlternativeLineSyncDto> StockAlternativeLines { get; set; } = new();

    public DateTimeOffset ServerTime { get; set; } = DateTimeOffset.UtcNow;
}

// =========================================================================
// 3. СПРАВОЧНИКИ, ПОЛЬЗОВАТЕЛИ И РОЛИ
// =========================================================================

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
    public string? Password { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public string? Description { get; set; }
    public List<string> Roles { get; set; } = new();
    public Dictionary<string, int>? AccountPrivileges { get; set; } = new();
}

// =========================================================================
// 4. ТОВАРЫ, ЦЕНЫ И ОСТАТКИ
// =========================================================================

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

// =========================================================================
// 5. ДОКУМЕНТЫ
// =========================================================================

public class InvoiceSyncDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = "Sales";
    public DateTime Date { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Description { get; set; }
    public List<InvoiceLineSyncDto> Lines { get; set; } = new();
    public List<InvoicePaymentSyncDto> Payments { get; set; } = new();
    public List<InvoiceDiscountSyncDto> Discounts { get; set; } = new();
}

public class InvoiceLineSyncDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

public class InvoicePaymentSyncDto
{
    public Guid Id { get; set; }
    public string PaymentType { get; set; } = "Payment";
    public decimal Amount { get; set; }
    public Guid? CurrencyId { get; set; }
    public int SortOrder { get; set; }
}

public class InvoiceDiscountSyncDto
{
    public Guid Id { get; set; }
    public string DiscountType { get; set; } = "Flat";
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class StockSlipSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public string SlipType { get; set; } = "Incoming";
    public DateTime Date { get; set; }
    public Guid StockId { get; set; }
    public string? Description { get; set; }
    public List<StockSlipLineSyncDto> Lines { get; set; } = new();
}

public class StockSlipLineSyncDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
}

public class FundsSlipSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public string SlipType { get; set; } = "FundsOpening";
    public DateTime Date { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? OfficeId { get; set; }
    public Guid? DepositoryId { get; set; }
    public Guid? PartnerId { get; set; }
    public Guid? DisplayCurrencyId { get; set; }
    public string? Description { get; set; }
    public List<FundsSlipLineSyncDto> Lines { get; set; } = new();
}

public class FundsSlipLineSyncDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public Guid? CurrencyId { get; set; }
    public int SortOrder { get; set; }
}

public class FundsTransferSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? FromDepositoryId { get; set; }
    public Guid? ToDepositoryId { get; set; }
    public Guid? DisplayCurrencyId { get; set; }
    public string? Description { get; set; }
    public List<FundsTransferLineSyncDto> Lines { get; set; } = new();
}

public class FundsTransferLineSyncDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public decimal ReceivedAmount { get; set; }
    public Guid? CurrencyId { get; set; }
    public int SortOrder { get; set; }
}

public class DailyFundsRegisterySyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? DepositoryId { get; set; }
    public Guid? DisplayCurrencyId { get; set; }
    public string? Description { get; set; }
    public List<DailyFundsRegisteryLineSyncDto> Lines { get; set; } = new();
}

public class DailyFundsRegisteryLineSyncDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public Guid? CurrencyId { get; set; }
    public int SortOrder { get; set; }
}

public class ExpenseSlipSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? OfficeId { get; set; }
    public Guid? DepositoryId { get; set; }
    public Guid? DisplayCurrencyId { get; set; }
    public string? Description { get; set; }
    public List<ExpenseSlipLineSyncDto> Lines { get; set; } = new();
}

public class ExpenseSlipLineSyncDto
{
    public Guid Id { get; set; }
    public Guid? ExpenseId { get; set; }
    public decimal Amount { get; set; }
    public Guid? CurrencyId { get; set; }
    public int SortOrder { get; set; }
}

public class PartnerSlipSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string SlipType { get; set; } = string.Empty;
    public Guid? OfficeId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Description { get; set; }
    public List<PartnerSlipLineSyncDto> Lines { get; set; } = new();
}

public class PartnerSlipLineSyncDto
{
    public Guid Id { get; set; }
    public Guid? PartnerId { get; set; }
    public decimal DebitAmount { get; set; }
    public Guid? DebitCurrencyId { get; set; }
    public decimal CreditAmount { get; set; }
    public Guid? CreditCurrencyId { get; set; }
}

public class PartnerTransferSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Description { get; set; }
    public List<PartnerTransferLineSyncDto> Lines { get; set; } = new();
}

public class PartnerTransferLineSyncDto
{
    public Guid Id { get; set; }
    public Guid? OfficeId { get; set; }
    public Guid? PartnerId { get; set; }
    public decimal DebitAmount { get; set; }
    public Guid? DebitCurrencyId { get; set; }
    public decimal CreditAmount { get; set; }
    public Guid? CreditCurrencyId { get; set; }
}

public class StockTransferSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? DestinationWarehouseId { get; set; }
    public Guid? DisplayCurrencyId { get; set; }
    public string? UserName { get; set; }
    public string? Description { get; set; }
    public decimal ActionTotal { get; set; }
    public decimal ActionReceivedTotal { get; set; }
    public List<StockTransferLineSyncDto> Lines { get; set; } = new();
}

public class StockTransferLineSyncDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? ReceivedUnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ActionTotal { get; set; }
    public decimal ActionReceivedTotal { get; set; }
    public int SortOrder { get; set; }
}

public class StockRevisionSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? FinishDate { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? ExceedSlipId { get; set; }
    public Guid? DeficitSlipId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Description { get; set; }
    public List<StockRevisionLineSyncDto> Lines { get; set; } = new();
}

public class StockRevisionLineSyncDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public DateTime Date { get; set; }
    public decimal Quantity { get; set; }
    public Guid? UnitId { get; set; }
    public decimal? Price { get; set; }
    public Guid? CurrencyId { get; set; }
}

public class StockOrderSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? PartnerId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Description { get; set; }
    public List<StockOrderLineSyncDto> Lines { get; set; } = new();
}

public class StockOrderLineSyncDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public Guid? UnitId { get; set; }
}

public class AggregatedStockOrderSyncDto
{
    public Guid Id { get; set; }
    public string SlipNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? PartnerId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Description { get; set; }
    public List<AggregatedStockOrderLineSyncDto> Lines { get; set; } = new();
}

public class AggregatedStockOrderLineSyncDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? UnitId { get; set; }
    public string OrdersJson { get; set; } = "{}";
}