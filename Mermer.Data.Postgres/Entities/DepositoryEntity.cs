using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("depositories")]
public class DepositoryEntity
{
    [Key][Column("id")] public Guid Id { get; set; }
    [Column("office_id")] public Guid? OfficeId { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("description")] public string? Description { get; set; }
    [Column("tags")] public string[]? Tags { get; set; }
    [Column("is_disabled")] public bool IsDisabled { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }

    [ForeignKey(nameof(OfficeId))]
    public OfficeEntity? Office { get; set; }
}