using System;
using System.Collections.Generic;
using System.Linq;

namespace Mermer.Commerce.Models;

public class InvoiceInfo
{
    public string Id { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = "admin";

    public bool IsCash { get; set; }

    public bool IsCompleted { get; set; }

    public bool IsDisabled { get; set; }

    public string Group { get; set; } = string.Empty;

    private IEnumerable<string> _tags = Enumerable.Empty<string>();
    public IEnumerable<string> Tags
    {
        get => _tags ?? Enumerable.Empty<string>();
        set => _tags = value ?? Enumerable.Empty<string>();
    }

    public string OfficeId { get; set; } = string.Empty;

    public string WarehouseId { get; set; } = string.Empty;

    public string DepositoryId { get; set; } = string.Empty;

    public string PartnerId { get; set; } = string.Empty;

    // Основные свойства, которые приходят из JSON и используются в XAML:
    public decimal ActionTotal { get; set; }

    public decimal Total
    {
        get => ActionTotal;
        set => ActionTotal = value;
    }

    public decimal ActionDiscountsTotal { get; set; }

    public decimal DiscountsTotal
    {
        get => ActionDiscountsTotal;
        set => ActionDiscountsTotal = value;
    }

    public decimal ActionGrandTotal { get; set; }

    public decimal GrandTotal
    {
        get => ActionGrandTotal;
        set => ActionGrandTotal = value;
    }
}