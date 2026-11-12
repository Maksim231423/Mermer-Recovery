using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("expenses")]
public class ExpenseEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("type")] public string? Type { get; set; }
    [Column("group_name")] public string? Group { get; set; }
    [Column("description")] public string? Description { get; set; }
    [Column("tags", TypeName = "text[]")] public string[]? Tags { get; set; }
    [Column("is_disabled")] public bool IsDisabled { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}