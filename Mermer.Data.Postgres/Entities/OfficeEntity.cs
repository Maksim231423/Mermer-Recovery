using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("offices")]
public class OfficeEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("region")]
    public string? Region { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public ICollection<WarehouseEntity> Warehouses { get; set; } = new List<WarehouseEntity>();
    public ICollection<DepositoryEntity> Depositories { get; set; } = new List<DepositoryEntity>();
}