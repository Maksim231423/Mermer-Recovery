using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("users")]
public class UserEntity
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Column("password")]
    public string Password { get; set; } = string.Empty;

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    // Список ID ролей, назначенных пользователю
    [Column("roles")]
    public List<string> Roles { get; set; } = new();

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}