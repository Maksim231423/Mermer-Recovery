// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.CRM.PartnerMergerDialogViewModel
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Ui.Core.Helpers;
using Mermer.Data.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.CRM;

public class PartnerMergerDialogViewModel : DialogViewModel
{
  private readonly IPartnersRepository _repository;
  private ObservableCollection<PartnerMerge> _list;
  private ObservableCollection<PartnerMerge> _selectedItems;
  private bool _disableMergedItems = true;

  public PartnerMergerDialogViewModel(
    IMvxMessenger messenger,
    Reference<Partner> partners,
    IPartnersRepository repository,
    IMvxNavigationService navigationService,
    IUserInteractionService userInteractionService)
    : base(messenger, navigationService, userInteractionService)
  {
    this.Partners = partners;
    this._repository = repository;
  }

  public Reference<Partner> Partners { get; }

  public virtual ObservableCollection<PartnerMerge> List
  {
    get => this._list;
    set
    {
      if (this._list != null)
        this._list.CollectionChanged -= new NotifyCollectionChangedEventHandler(this.List_CollectionChanged);
      this.SetProperty<ObservableCollection<PartnerMerge>>(ref this._list, value, nameof (List));
      if (this._list != null)
        this._list.CollectionChanged += new NotifyCollectionChangedEventHandler(this.List_CollectionChanged);
      this.RaisePropertyChanged<bool>((Expression<Func<bool>>) (() => this.HasAnyItems));
    }
  }

  public bool HasAnyItems
  {
    get
    {
      ObservableCollection<PartnerMerge> list = this.List;
      return list != null && list.Any<PartnerMerge>();
    }
  }

  public ObservableCollection<PartnerMerge> SelectedItems
  {
    get => this._selectedItems;
    set
    {
      if (this._selectedItems != null)
        this._selectedItems.CollectionChanged -= new NotifyCollectionChangedEventHandler(this.SelectedItems_CollectionChanged);
      this.SetProperty<ObservableCollection<PartnerMerge>>(ref this._selectedItems, value, nameof (SelectedItems));
      if (this._selectedItems != null)
        this._selectedItems.CollectionChanged += new NotifyCollectionChangedEventHandler(this.SelectedItems_CollectionChanged);
      this.RaisePropertyChanged<bool>((Expression<Func<bool>>) (() => this.HasAnyItemsSelected));
    }
  }

  public bool HasAnyItemsSelected
  {
    get
    {
      ObservableCollection<PartnerMerge> selectedItems = this.SelectedItems;
      return selectedItems != null && selectedItems.Any<PartnerMerge>();
    }
  }

  public virtual bool DisableMergedItems
  {
    get => this._disableMergedItems;
    set => this.SetProperty<bool>(ref this._disableMergedItems, value, nameof (DisableMergedItems));
  }

  private void List_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
  {
    if (e.OldItems != null)
    {
      foreach (BindableObject bindableObject in e.OldItems.Cast<PartnerMerge>())
        bindableObject.PropertyChanged -= new PropertyChangedEventHandler(this.Item_PropertyChanged);
    }
    if (e.NewItems != null)
    {
      foreach (BindableObject bindableObject in e.NewItems.Cast<PartnerMerge>())
        bindableObject.PropertyChanged += new PropertyChangedEventHandler(this.Item_PropertyChanged);
    }
    this.RaisePropertyChanged<bool>((Expression<Func<bool>>) (() => this.HasAnyItems));
  }

  private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
  {
    if (!(e.PropertyName == "IsMain"))
      return;
    PartnerMerge item = sender as PartnerMerge;
    if (item == null || !item.IsMain)
      return;
    foreach (PartnerMerge partnerMerge in this.List.Where<PartnerMerge>((Func<PartnerMerge, bool>) (x => x.PartnerId != item.PartnerId)))
      partnerMerge.IsMain = false;
  }

  private void SelectedItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
  {
    this.RaisePropertyChanged<bool>((Expression<Func<bool>>) (() => this.HasAnyItemsSelected));
  }

  protected override Task PreLoad() => Task.WhenAll(base.PreLoad(), this.Partners.Initialize());

  protected override Task OnLoad()
  {
    this.List = new ObservableCollection<PartnerMerge>();
    this.SelectedItems = new ObservableCollection<PartnerMerge>();
    return base.OnLoad();
  }

  public ICommand SelectedItemsDeleteCommand
  {
    get
    {
      return (ICommand) new MvxCommand(new Action(this.OnSelectedItemsDeleteCommand), (Func<bool>) (() => !this.IsBusy && this.HasAnyItemsSelected));
    }
  }

  protected virtual void OnSelectedItemsDeleteCommand()
  {
    this.IsBusy = true;
    try
    {
      PartnerMerge[] array = this.SelectedItems.ToArray<PartnerMerge>();
      this.SelectedItems = new ObservableCollection<PartnerMerge>();
      foreach (PartnerMerge partnerMerge in array)
        this.List.Remove(partnerMerge);
    }
    catch (Exception ex)
    {
      this.UserInteractionService.ShowExceptionMessage(ex);
    }
    this.IsBusy = false;
  }

  public ICommand MergeCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnMergeAsync), (Func<bool>) (() => !this.IsBusy && this.HasAnyItems));
    }
  }

    public virtual async Task OnMergeAsync()
    {
        PartnerMergerDialogViewModel mergerDialogViewModel = this;
        mergerDialogViewModel.IsBusy = true;
        try
        {
            if (mergerDialogViewModel.List.Count <= 1)
                throw new Exception(mergerDialogViewModel["Invalid Operation", Array.Empty<object>()], new Exception(mergerDialogViewModel["At least two items must be selected", Array.Empty<object>()]));

            string partnerId = mergerDialogViewModel.List.Single<PartnerMerge>((Func<PartnerMerge, bool>)(x => x.IsMain)).PartnerId;
            string[] array = mergerDialogViewModel.List.Where<PartnerMerge>((Func<PartnerMerge, bool>)(x => !x.IsMain)).Select<PartnerMerge, string>((Func<PartnerMerge, string>)(x => x.PartnerId)).Distinct<string>().ToArray<string>();

            await mergerDialogViewModel._repository.MergeAsync(partnerId, array, mergerDialogViewModel.DisableMergedItems);

            // Закрываем диалог слияния после успешной операции
            await mergerDialogViewModel.NavigationService.Close(mergerDialogViewModel);
        }
        catch (InvalidOperationException ex)
        {
            mergerDialogViewModel.UserInteractionService.ShowMessage(mergerDialogViewModel["Invalid Operation", Array.Empty<object>()], mergerDialogViewModel["One item (only) must be selected as Main", Array.Empty<object>()]);
        }
        catch (Exception ex)
        {
            mergerDialogViewModel.UserInteractionService.ShowExceptionMessage(ex);
        }
        finally
        {
            mergerDialogViewModel.IsBusy = false;
        }
    }
}
