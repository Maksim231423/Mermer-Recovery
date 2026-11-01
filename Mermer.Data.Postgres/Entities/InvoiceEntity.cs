using System;
using System.Collections.Generic;

namespace Mermer.Data.Postgres.Entities;

/// <summary>
/// Invoice entity — sales/purchase invoice.
/// Maps to: Mermer.Commerce.Models.Invoice (inherits StockTransaction → Transaction → TransactionModel)
/// </summary>
public class InvoiceEntity
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public DateTimeOffset Date { get; set; }
    public DateTimeOffset? DueDate { get; set; }

    /// <summary>Purchase, PurchaseReturn, Sales, SalesReturn</summary>
    public string InvoiceType { get; set; } = "Sales";

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? OfficeId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? DepositoryId { get; set; }
    public Guid? PartnerId { get; set; }
    public Guid? DisplayCurrencyId { get; set; }
    public string? StockPriceGroup { get; set; }
    public bool DebitCreditLeftAmount { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsDisabled { get; set; }
    public string? Group { get; set; }
    public string[]? Tags { get; set; }
    public string? Description { get; set; }
    public bool IsSynced { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public OfficeEntity? Office { get; set; }
    public WarehouseEntity? Warehouse { get; set; }
    public DepositoryEntity? Depository { get; set; }
    public PartnerEntity? Partner { get; set; }
    public CurrencyEntity? DisplayCurrency { get; set; }

    public ICollection<InvoiceLineEntity> Lines { get; set; } = new List<InvoiceLineEntity>();
    public ICollection<InvoiceDiscountEntity> Discounts { get; set; } = new List<InvoiceDiscountEntity>();
    public ICollection<InvoicePaymentEntity> Payments { get; set; } = new List<InvoicePaymentEntity>();
    public ICollection<InvoiceCurrencyConvertionEntity> CurrencyConvertions { get; set; } = new List<InvoiceCurrencyConvertionEntity>();
    public ICollection<InvoiceStockUnitConvertionEntity> StockUnitConvertions { get; set; } = new List<InvoiceStockUnitConvertionEntity>();
    public ICollection<InvoiceOverheadEntity> Overheads { get; set; } = new List<InvoiceOverheadEntity>();
}
