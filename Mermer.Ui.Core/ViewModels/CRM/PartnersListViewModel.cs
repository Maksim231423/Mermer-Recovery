// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.CRM.PartnersListViewModel
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Plugins.Messenger;
using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Ui.Core.ViewModels.Common;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.CRM;

public class PartnersListViewModel : ListViewModel<Partner>
{
  private readonly IPartnerCodeGenerationService _codeGenerationService;

  public PartnersListViewModel(
    IMvxMessenger messenger,
    IRepository<Partner> repository,
    IListAuthorizer<Partner> authorizer,
    IMvxNavigationService navigationService,
    IUserInteractionService userInteractionService,
    IPartnerCodeGenerationService codeGenerationService)
    : base(repository, authorizer, messenger, navigationService, userInteractionService)
  {
    this._codeGenerationService = codeGenerationService;
  }

  public ICommand ImportCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnImportCommandAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

    private ICommand _reloadCommand;
    public new ICommand ReloadCommand => _reloadCommand ??= new MvxAsyncCommand(OnReloadAsync, () => !this.IsBusy);

    private async Task OnReloadAsync()
    {
        this.IsBusy = true;
        try
        {
            // Вызываем ReloadAsync через рефлексию, если репозиторий его поддерживает
            var reloadMethod = this.Repository?.GetType().GetMethod("ReloadAsync");
            if (reloadMethod != null)
            {
                var task = reloadMethod.Invoke(this.Repository, null) as Task;
                if (task != null) await task;
            }

            await this.Initialize();
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    protected virtual async Task OnImportCommandAsync()
  {
    PartnersListViewModel partnersListViewModel = this;
    IEnumerable<object> source1 = await partnersListViewModel.NavigationService.Navigate<DataImportViewModel, Type, IEnumerable<object>>(typeof (PartnersListViewModel.PartnerImport));
    int i = 0;
    partnersListViewModel.IsBusy = true;
    partnersListViewModel.SuspendLoading = true;
    try
    {
      IEnumerable<PartnersListViewModel.PartnerImport> source2 = source1 != null ? source1.Cast<PartnersListViewModel.PartnerImport>() : (IEnumerable<PartnersListViewModel.PartnerImport>) null;
      if (source2 != null)
      {
        int itemsCount = source2.Count<PartnersListViewModel.PartnerImport>();
        foreach (PartnersListViewModel.PartnerImport partnerImport in source2)
        {
          PartnersListViewModel.PartnerImport item = partnerImport;
          ++i;
          partnersListViewModel.Status = partnersListViewModel["Importing {0} of {1} items", new object[2]
          {
            (object) i,
            (object) itemsCount
          }];
          bool exists = true;
          Partner model = (Partner) null;
          if (!string.IsNullOrEmpty(item.Code))
            model = (await partnersListViewModel.Repository.GetAsync((Expression<Func<Partner, bool>>) (x => x.Code == item.Code))).FirstOrDefault<Partner>();
          if (model == null)
          {
            exists = false;
            Partner partner1 = new Partner();
            partner1.Id = Guid.NewGuid().ToString();
            Partner partner2 = partner1;
            string str = item.Code;
            if (str == null)
              str = await partnersListViewModel._codeGenerationService.GetNextCode();
            partner2.Code = str;
            partner1.Tags = (IEnumerable<string>) new string[0];
            model = partner1;
            partner2 = (Partner) null;
            partner1 = (Partner) null;
          }
          if (!string.IsNullOrEmpty(item.Name))
            model.Name = item.Name;
          if (!string.IsNullOrEmpty(item.Group))
            model.Group = item.Group;
          if (!string.IsNullOrEmpty(item.Tags))
            model.Tags = ((IEnumerable<string>) ((object) model.Tags ?? (object) new string[0])).Union<string>(((IEnumerable<string>) item.Tags.Split(',')).Select<string, string>((Func<string, string>) (x => x.Trim())).Where<string>((Func<string, bool>) (x => !string.IsNullOrEmpty(x)))).Distinct<string>();
          if (exists)
            await partnersListViewModel.Repository.UpdateAsync(model);
          else
            await partnersListViewModel.Repository.CreateAsync(model);
        }
      }
    }
    catch (Exception ex)
    {
      partnersListViewModel.UserInteractionService.ShowExceptionMessage(ex);
    }
    partnersListViewModel.Status = (string) null;
    partnersListViewModel.SuspendLoading = false;
    partnersListViewModel.IsBusy = false;
    partnersListViewModel.ReloadCommand.Execute((object) null);
  }

  public ICommand MergeCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnMergeAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

  public virtual Task OnMergeAsync()
  {
    return this.NavigationService.Navigate<PartnerMergerDialogViewModel>();
  }

  public class PartnerImport
  {
    public string Code { get; internal set; }

    public string Name { get; internal set; }

    public string Group { get; internal set; }

    public string Tags { get; internal set; }
  }
}
