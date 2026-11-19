// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.Authorization.RoleAction
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using Mermer.Authorization.Models;
using Mermer.Data.Models;
using System.Collections.Generic;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Authorization;

public class RoleAction : BindableObject
{
    private readonly Role _role;

    public RoleAction(Role role) => this._role = role;

    public virtual string Id { get; set; }

    public virtual string Name { get; set; }

    // Проверяет, сохранял ли пользователь это действие
    public virtual bool IsConfigured =>
        this._role.Authorizations != null && this._role.Authorizations.ContainsKey(this.Id);

    public virtual int SeletedValue
    {
        get => !IsConfigured ? -1 : this._role.Authorizations[this.Id];
        set
        {
            if (this._role.Authorizations == null)
                this._role.Authorizations = new Dictionary<string, int>();

            this._role.Authorizations[this.Id] = value;
            this.RaisePropertyChanged(nameof(SeletedValue));
            this.RaisePropertyChanged(nameof(IsConfigured));

            if (this.Options != null)
            {
                foreach (BindableObject option in this.Options)
                    option.RaisePropertyChanged("IsSelected");
            }
        }
    }

    public virtual IEnumerable<RoleOption> Options { get; set; }
}