using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("invoices")]
public class InvoiceEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("code")]
    public string? Code { get; set; }

    [Column("date")]
    public DateTimeOffset Date { get; set; }

    [Column("due_date")]
    public DateTimeOffset? DueDate { get; set; }

    [Column("invoice_type")]
    public string InvoiceType { get; set; } = "Sales";

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("office_id")]
    public Guid? OfficeId { get; set; }

    [Column("warehouse_id")]
    public Guid? WarehouseId { get; set; }

    [Column("depository_id")]
    public Guid? DepositoryId { get; set; }

    [Column("partner_id")]
    public Guid? PartnerId { get; set; }

    [Column("display_currency_id")]
    public Guid? DisplayCurrencyId { get; set; }

    [Column("stock_price_group")]
    public string? StockPriceGroup { get; set; }

    [Column("debit_credit_left_amount")]
    public bool DebitCreditLeftAmount { get; set; }

    [Column("is_completed")]
    public bool IsCompleted { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("group_name")]
    public string? Group { get; set; }

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("is_synced")]
    public bool IsSynced { get; set; } = false;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(OfficeId))]
    public OfficeEntity? Office { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public WarehouseEntity? Warehouse { get; set; }

    [ForeignKey(nameof(DepositoryId))]
    public DepositoryEntity? Depository { get; set; }

    [ForeignKey(nameof(PartnerId))]
    public PartnerEntity? Partner { get; set; }

    [ForeignKey(nameof(DisplayCurrencyId))]
    public CurrencyEntity? DisplayCurrency { get; set; }

    public ICollection<InvoiceLineEntity> Lines { get; set; } = new List<InvoiceLineEntity>();
    public ICollection<InvoiceDiscountEntity> Discounts { get; set; } = new List<InvoiceDiscountEntity>();
    public ICollection<InvoicePaymentEntity> Payments { get; set; } = new List<InvoicePaymentEntity>();
    public ICollection<InvoiceCurrencyConvertionEntity> CurrencyConvertions { get; set; } = new List<InvoiceCurrencyConvertionEntity>();
    public ICollection<InvoiceStockUnitConvertionEntity> StockUnitConvertions { get; set; } = new List<InvoiceStockUnitConvertionEntity>();
    public ICollection<InvoiceOverheadEntity> Overheads { get; set; } = new List<InvoiceOverheadEntity>();
}