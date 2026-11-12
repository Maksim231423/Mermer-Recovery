using MvvmCross.Plugins.Messenger;
using Mermer.Data.Models;
using Mermer.Data.Storage;
using Mermer.Mvvm.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

#nullable disable
namespace Mermer.Ui.Core.Helpers;

public class Reference<T> : BindableObject, IDisposable where T : IModel
{
    private readonly IRepository<T> _repository;
    private readonly IMvxMessenger _messenger;
    private MvxSubscriptionToken _messageToken;
    private List<T> _rawList;
    private List<T> _filteredList;
    private Func<T, bool> _filter;
    private bool _SuspendLoading;
    public bool _isLoaded;

    public Reference(IRepository<T> repository, IMvxMessenger messenger)
    {
        this._repository = repository;
        this._messenger = messenger;
        this._messageToken = this._messenger.Subscribe<DocumentModified<T>>((Action<DocumentModified<T>>)(async m =>
        {
            if (!this._isLoaded)
                return;
            await this.Initialize();
        }), MvxReference.Strong);
    }

    public virtual IEnumerable<T> List
    {
        get => this._filteredList ?? (IEnumerable<T>)this._rawList ?? Array.Empty<T>();
        set
        {
            this._rawList = value as List<T> ?? value?.ToList();
            UpdateFilteredList();
            this.RaisePropertyChanged(nameof(List));
        }
    }

    public Func<T, bool> Filter
    {
        get => this._filter;
        set
        {
            this._filter = value;
            UpdateFilteredList();
            this.RaisePropertyChanged(nameof(Filter));
            this.RaisePropertyChanged(nameof(List));
        }
    }

    public bool SuspendLoading
    {
        get => this._SuspendLoading;
        set => this.SetProperty<bool>(ref this._SuspendLoading, value, nameof(SuspendLoading));
    }

    private void UpdateFilteredList()
    {
        if (this._rawList == null)
        {
            this._filteredList = null;
            return;
        }

        this._filteredList = this._filter != null
            ? this._rawList.Where(this._filter).ToList()
            : this._rawList;
    }

    public virtual async Task Initialize()
    {
        if (this.SuspendLoading || this._isLoaded)
            return;

        var items = await this._repository.GetAsync();
        this._rawList = items as List<T> ?? items?.ToList();
        UpdateFilteredList();
        this._isLoaded = true;
        this.RaisePropertyChanged(nameof(List));
    }

    public void Dispose() => this._messageToken?.Dispose();
}