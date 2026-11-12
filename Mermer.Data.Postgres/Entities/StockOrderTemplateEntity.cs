using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("stock_order_templates")]
public class StockOrderTemplateEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("group_name")]
    public string? GroupName { get; set; }

    [Column("tags")]
    public string[]? Tags { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<StockOrderTemplateLineEntity> Lines { get; set; } = new List<StockOrderTemplateLineEntity>();
}