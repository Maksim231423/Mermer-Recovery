// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.Helpers.Reference`1
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

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
  private IEnumerable<T> _list;
  private Func<T, bool> _filter;
  private bool _SuspendLoading;
  public bool _isLoaded;

  public Reference(IRepository<T> repository, IMvxMessenger messenger)
  {
    this._repository = repository;
    this._messenger = messenger;
    this._messageToken = this._messenger.Subscribe<DocumentModified<T>>((Action<DocumentModified<T>>) (async m =>
    {
      if (!this._isLoaded)
        return;
      await this.Initialize();
    }), MvxReference.Strong);
  }

  public virtual IEnumerable<T> List
  {
    get
    {
      if (this.Filter == null)
        return this._list;
      IEnumerable<T> list = this._list;
      return list == null ? (IEnumerable<T>) null : list.Where<T>(this.Filter);
    }
    set => this.SetProperty<IEnumerable<T>>(ref this._list, value, nameof (List));
  }

  public Func<T, bool> Filter
  {
    get => this._filter;
    set
    {
      this._filter = value;
      this.RaisePropertyChanged(nameof (Filter));
      this.RaisePropertyChanged("List");
    }
  }

  public bool SuspendLoading
  {
    get => this._SuspendLoading;
    set => this.SetProperty<bool>(ref this._SuspendLoading, value, nameof (SuspendLoading));
  }

  public virtual async Task Initialize()
{
    if (this.SuspendLoading || this._isLoaded) // Если уже загружено — пропускаем!
        return;
        
    this.List = await this._repository.GetAsync();
    this._isLoaded = true;
}

  public void Dispose() => this._messageToken?.Dispose();
}
