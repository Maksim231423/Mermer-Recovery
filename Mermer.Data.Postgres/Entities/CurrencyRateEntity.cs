using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("currency_rates")]
public class CurrencyRateEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("currency_id")] public Guid CurrencyId { get; set; }
    [Column("valid_from")] public DateTime ValidFrom { get; set; }
    [Column("multiplier")] public decimal Multiplier { get; set; } = 1m;
    [Column("divider")] public decimal Divider { get; set; } = 1m;
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey(nameof(CurrencyId))]
    public CurrencyEntity Currency { get; set; } = null!;
}