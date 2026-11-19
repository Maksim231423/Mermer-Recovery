using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Mermer.Data.Postgres.Entities;

[Table("users")]
public class UserEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Key]
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

    [Column("roles")]
    public List<string> Roles { get; set; } = new();

    [Column("account_privileges")]
    public string? AccountPrivilegesJson { get; set; }

    [NotMapped]
    public Dictionary<string, int> AccountPrivileges
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AccountPrivilegesJson))
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(AccountPrivilegesJson, JsonOptions);
                return dict != null
                    ? new Dictionary<string, int>(dict, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }
        set
        {
            AccountPrivilegesJson = value != null ? JsonSerializer.Serialize(value) : "{}";
        }
    }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}