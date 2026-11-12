using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("partner_transfers")]
public class PartnerTransferEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("code")]
    public string Code { get; set; } = string.Empty;

    [Column("date")]
    public DateTime Date { get; set; }

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

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

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public UserEntity? User { get; set; }

    public ICollection<PartnerTransferLineEntity> Lines { get; set; } = new List<PartnerTransferLineEntity>();
}