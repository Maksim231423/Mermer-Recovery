namespace Mermer.Api.Services;

public class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// Если false — программа работает на 100% автономно на локальном Postgres, сетевой обмен не запускается.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Адрес центрального сервера VDS (например, https://vds-server:5050).
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Интервал фонового опроса VDS (в секундах).
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 30;
}