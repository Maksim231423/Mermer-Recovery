using System;
using System.Collections.Generic;

namespace Mermer.Api.DTOs;

public class SyncPushRequestDto
{
    public string ClientId { get; set; } = string.Empty;
    public List<InvoiceSyncDto> Invoices { get; set; } = new();
    public List<StockSlipSyncDto> StockSlips { get; set; } = new();
}

public class InvoiceSyncDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal TotalAmount { get; set; }
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
    public string SlipType { get; set; } = string.Empty;
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

public class SyncPushResponseDto
{
    public bool Success { get; set; }
    public int ProcessedInvoicesCount { get; set; }
    public int ProcessedStockSlipsCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}