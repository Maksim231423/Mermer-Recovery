// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.CRM.PartnerDetailsViewModel
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using Mermer.CRM.Models;
using Mermer.CRM.Services;
using Mermer.Data.Authorizers;
using Mermer.Data.Storage;
using Mermer.FundsManagement.Models;
using Mermer.Mvvm.Services;
using Mermer.Mvvm.ViewModels;
using Mermer.Ui.Core.Helpers;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.CRM;

public class PartnerDetailsViewModel : DetailsViewModel<Partner>
{
  private readonly IPartnerCodeGenerationService _codeGenerator;
  private string[] _groupNames;
  private string[] _tagNames;

  public PartnerDetailsViewModel(
    Reference<Currency> currencies,
    IRepository<Partner> repository,
    IListAuthorizer<Partner> authorizer,
    IMvxNavigationService navigationService,
    IPartnerCodeGenerationService codeGenerator,
    IUserInteractionService userInteractionService)
    : base(repository, authorizer, navigationService, userInteractionService)
  {
    this._codeGenerator = codeGenerator;
    this.Currencies = currencies;
  }

  public Reference<Currency> Currencies { get; set; }

  public virtual string[] GroupNames
  {
    get => this._groupNames;
    set => this.SetProperty<string[]>(ref this._groupNames, value, nameof (GroupNames));
  }

  public virtual string[] TagNames
  {
    get => this._tagNames;
    set => this.SetProperty<string[]>(ref this._tagNames, value, nameof (TagNames));
  }

  protected virtual async Task LoadFacetsAsync()
  {
    PartnerDetailsViewModel detailsViewModel = this;
    Dictionary<string, Dictionary<string, int>> facets = await ((IRepositoryWithFacets<Partner>) detailsViewModel.Repository).GetFacets("GroupNames", "TagNames");
    detailsViewModel.GroupNames = facets["GroupNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>) (x => x.Key)).ToArray<string>();
    detailsViewModel.TagNames = facets["TagNames"].Select<KeyValuePair<string, int>, string>((Func<KeyValuePair<string, int>, string>) (x => x.Key)).ToArray<string>();
  }

  protected override Task PreLoad()
  {
    return Task.WhenAll(base.PreLoad(), this.LoadFacetsAsync(), this.Currencies.Initialize());
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
            await this.LoadFacetsAsync();
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    protected override async Task PostLoad()
    {
        await base.PostLoad();

        if (!string.IsNullOrEmpty(Details.Code))
            return;

        Details.Code = await _codeGenerator.GetNextCode();
    }
    protected override async Task<bool> OnSaveAsync()
    {
        try
        {
            
            if (string.IsNullOrWhiteSpace(Details.Name))
            {
                throw new Exception(this["Field '{0}' is required", this["Name"]]);
            }

            // Проверка телефона:
            // - NullOrWhiteSpace проверяет поле не пустое
            // - Regex проверяет ли это строка, состоящая только из цифр, 
            // возможно с '+' в начале и длиной от 7 до 15 знаков.
            string phonePattern = @"^\+?[0-9]{7,15}$";

            if (string.IsNullOrWhiteSpace(Details.Phone))
            {
                throw new Exception(this["Field '{0}' is required", this["Phone"]]);
            }
            else if (!Regex.IsMatch(Details.Phone, phonePattern))
            {
                
                throw new Exception(this["Field '{0}' is invalid", this["Phone"]]);
            }
        }
        catch (Exception ex)
        {
            UserInteractionService.ShowExceptionMessage(ex);
            return false;
        }

        return await base.OnSaveAsync();
    }
}
