using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("currencies")]
public class CurrencyEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("decimals")] public int Decimals { get; set; } = 2;
    [Column("is_default")] public bool IsDefault { get; set; }
    [Column("description")] public string? Description { get; set; }
    [Column("is_disabled")] public bool IsDisabled { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<CurrencyRateEntity> Rates { get; set; } = new List<CurrencyRateEntity>();
}