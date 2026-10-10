using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Mermer.Data.Postgres.Entities;

[Table("roles")]
public class RoleEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("authorizations")]
    public string? AuthorizationsJson { get; set; }

    [NotMapped]
    public Dictionary<string, int> Authorizations
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AuthorizationsJson))
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(AuthorizationsJson, JsonOptions);
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
            AuthorizationsJson = value != null ? JsonSerializer.Serialize(value) : "{}";
        }
    }

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

    [Column("is_disabled")]
    public bool IsDisabled { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}