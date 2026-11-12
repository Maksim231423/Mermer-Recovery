using System;
using System.Collections.Generic;

namespace Mermer.Api.DTOs;

public class SyncPushRequestDto
{
    public string ClientId { get; set; } = string.Empty;

    // 1. Торговля и склад
    public List<InvoiceSyncDto> Invoices { get; set; } = new();
    public List<StockSlipSyncDto> StockSlips { get; set; } = new();
    public List<StockTransferSyncDto> StockTransfers { get; set; } = new();
    public List<StockRevisionSyncDto> StockRevisions { get; set; } = new();
    public List<StockOrderSyncDto> StockOrders { get; set; } = new();
    public List<AggregatedStockOrderSyncDto> AggregatedStockOrders { get; set; } = new();

    // 2. Финансы и касса
    public List<FundsSlipSyncDto> FundsSlips { get; set; } = new();
    public List<FundsTransferSyncDto> FundsTransfers { get; set; } = new();
    public List<DailyFundsRegisterySyncDto> DailyFundsRegisteries { get; set; } = new();
    public List<ExpenseSlipSyncDto> ExpenseSlips { get; set; } = new();

    // 3. Взаиморасчеты с контрагентами
    public List<PartnerSlipSyncDto> PartnerSlips { get; set; } = new();
    public List<PartnerTransferSyncDto> PartnerTransfers { get; set; } = new();
    public List<PartnerSyncDto> Partners { get; set; } = new();
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
}

// ---------------- DTO ПРОДАЖ И СКЛАДСКИХ АКТОВ (INVOICES & STOCK SLIPS) ----------------
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

// ---------------- DTO ДЕНЕЖНЫХ СРЕДСТВ ----------------
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

// ---------------- DTO ВЗАИМОРАСЧЕТОВ ----------------
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

// ---------------- DTO ПЕРЕМЕЩЕНИЙ И РЕВИЗИЙ ----------------
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