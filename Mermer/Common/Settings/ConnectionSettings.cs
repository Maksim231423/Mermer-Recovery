using Mermer.Data.Models;
using System;
using System.Linq.Expressions;

#nullable disable
namespace Mermer.Common.Settings;

public class ConnectionSettings : BindableObject
{
    private int _mode;
    private string _serviceAddress;
    private string _databaseAddress;
    private string _databaseName;
    private string _databaseUser;
    private string _databasePassword;
    private bool _allowReporting = false; // ПО УМОЛЧАНИЮ FALSE (VDS ВЫКЛЮЧЕН)

    public virtual int Mode
    {
        get => this._mode;
        set
        {
            if (!this.SetProperty<int>(ref this._mode, value, nameof(Mode)))
                return;
            this.RaisePropertyChanged<bool>((Expression<Func<bool>>)(() => this.IsDirectModeSelected));
        }
    }

    public bool IsDirectModeSelected => this.Mode == 0;

    public virtual string ServiceAddress
    {
        // ИСПРАВЛЕНО: Дефолт строго 5050 вместо 8088
        get => this._serviceAddress ?? "http://127.0.0.1:5050";
        set => this.SetProperty<string>(ref this._serviceAddress, value, nameof(ServiceAddress));
    }

    public virtual string DatabaseAddress
    {
        // ИСПРАВЛЕНО: Дефолт строго 5050 вместо 8091
        get => this._databaseAddress ?? "http://127.0.0.1:5050";
        set => this.SetProperty<string>(ref this._databaseAddress, value, nameof(DatabaseAddress));
    }

    public virtual string DatabaseName
    {
        get => this._databaseName ?? "mermer";
        set => this.SetProperty<string>(ref this._databaseName, value, nameof(DatabaseName));
    }

    public virtual string DatabaseUser
    {
        get => this._databaseUser;
        set => this.SetProperty<string>(ref this._databaseUser, value, nameof(DatabaseUser));
    }

    public virtual string DatabasePassword
    {
        get => this._databasePassword;
        set => this.SetProperty<string>(ref this._databasePassword, value, nameof(DatabasePassword));
    }

    public virtual bool AllowReporting
    {
        get => this._allowReporting;
        set => this.SetProperty<bool>(ref this._allowReporting, value, nameof(AllowReporting));
    }

    public int SyncIntervalSeconds { get; set; } = 15;
}