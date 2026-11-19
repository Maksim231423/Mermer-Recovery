// Decompiled with JetBrains decompiler
// Type: Mermer.Ui.Core.ViewModels.Authorization.RoleOption
// Assembly: Mermer.Ui.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: DC92D011-8413-44AC-9F10-F866D891CF66
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Ui.Core.dll

using Mermer.Data.Models;
using System;

#nullable disable
namespace Mermer.Ui.Core.ViewModels.Authorization;

public class RoleOption : BindableObject
{
    private readonly RoleAction _action;

    public RoleOption(RoleAction action) => this._action = action;

    public virtual string Name { get; set; }

    public virtual int Value { get; set; }

    public virtual bool IsSelected
    {
        get
        {
            // Если действие вообще еще не настраивалось пользователем — ВСЕ чекбоксы пустые!
            if (!this._action.IsConfigured)
                return false;

            // Если пользователь явно выбрал Deny/None (0)
            if (this.Value == 0)
                return this._action.SeletedValue == 0;

            // Для битовых флагов (Create, ReadOwn и т.д.)
            return this._action.SeletedValue.HasBit(this.Value);
        }
        set
        {
            if (value)
            {
                if (this.Value == 0)
                    this._action.SeletedValue = 0; // явно выбрали Deny / None
                else
                {
                    // если до этого стоял 0, сбрасываем его и ставим выбранный флаг
                    int current = this._action.SeletedValue <= 0 ? 0 : this._action.SeletedValue;
                    this._action.SeletedValue = current.AddBit(this.Value);
                }
            }
            else
            {
                if (this.Value != 0 && this._action.SeletedValue > 0)
                {
                    this._action.SeletedValue = this._action.SeletedValue.RemoveBit(this.Value);
                }
                else if (this.Value == 0)
                {
                    // Пользователь снял галочку с Deny/None
                    this._action.SeletedValue = -1;
                }
            }
        }
    }
}
