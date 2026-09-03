using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mermer.Data.Postgres.Entities;

[Table("licenses")]
public class LicenseEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("key")]
    public string Key { get; set; } = ""; // Например: 6306d46e-888d-46ca-8e04-1746869bac96

    [Column("application_id")]
    public string ApplicationId { get; set; } = "55ddc105-8f48-4f78-b214-aea448d2a370";

    [Column("module_id")]
    public string ModuleId { get; set; } = ""; // "dc60017b-9b20-46ca-8b2e-646de9965a9e" (клиент) или "9a953aa5-2fd9-418d-bcf7-fb5bd7d09553" (сервер)

    [Column("machine_id")]
    public string? MachineId { get; set; } // Привязка к конкретному ПК

    [Column("note")]
    public string? Note { get; set; }

    [Column("valid_from")]
    public DateTimeOffset ValidFrom { get; set; }

    [Column("valid_till")]
    public DateTimeOffset? ValidTill { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}