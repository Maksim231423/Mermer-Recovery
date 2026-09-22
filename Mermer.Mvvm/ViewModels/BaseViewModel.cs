// Decompiled with JetBrains decompiler
// Type: Mermer.Mvvm.ViewModels.BaseViewModel
// Assembly: Mermer.Mvvm, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 3EAA5570-F618-4E39-B929-F7374F99B43D
// Assembly location: C:\Users\Admin\AppData\Local\Temp\Bofyhol\f9d7aa10a6\lib\net45\Mermer.Mvvm.dll

using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Localization;
using Mermer.Mvvm.Services;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;

#nullable disable
namespace Mermer.Mvvm.ViewModels;

public abstract class BaseViewModel : MvxViewModel, IDisposable
{
    // Глобальный триггер для жесткого закрытия
    public static Action<MvxViewModel> RequestComponentCloseAction { get; set; }
    protected readonly IMvxNavigationService NavigationService;
  protected readonly IUserInteractionService UserInteractionService;
  private string _caption;
  private string _subCaption;
  private string _status;
  private bool _isBusy;
  private bool _suspendLoading;

    protected BaseViewModel(
    IMvxNavigationService navigationService,
    IUserInteractionService userInteractionService)
  {
    this.NavigationService = navigationService;
    this.UserInteractionService = userInteractionService;
    this.PropertyChanged += new PropertyChangedEventHandler(this.ForceCommandCanExecuteChanges);
  }

  public virtual string Caption
  {
    get => this._caption ?? this[this.GetType().Name, Array.Empty<object>()];
    set => this.SetProperty<string>(ref this._caption, value, nameof (Caption));
  }

  public virtual string SubCaption
  {
    get => this._subCaption;
    set => this.SetProperty<string>(ref this._subCaption, value, nameof (SubCaption));
  }

  public string Status
  {
    get => this._status ?? this["Loading...", Array.Empty<object>()];
    set => this.SetProperty<string>(ref this._status, value, nameof (Status));
  }

  public virtual bool IsBusy
  {
    get => this._isBusy;
    set => this.SetProperty<bool>(ref this._isBusy, value, nameof (IsBusy));
  }

  public bool SuspendLoading
  {
    get => this._suspendLoading;
    set => this.SetProperty<bool>(ref this._suspendLoading, value, nameof (SuspendLoading));
  }

  public override async Task Initialize()
  {
    BaseViewModel viewModel = this;
    if (viewModel.SuspendLoading)
      return;
    viewModel.IsBusy = true;
    try
    {
      await viewModel.PreLoad();
      await viewModel.OnLoad();
      await viewModel.PostLoad();
    }
    catch (Exception ex)
    {
      viewModel.UserInteractionService.ShowExceptionMessage(ex, viewModel["Error loading {0}", new object[1]
      {
        (object) viewModel.Caption
      }]);
      viewModel.Close((IMvxViewModel) viewModel);
    }
    viewModel.IsBusy = false;
  }

  protected virtual Task PreLoad() => Task.CompletedTask;

  protected virtual Task OnLoad() => Task.CompletedTask;

  protected virtual Task PostLoad() => Task.CompletedTask;

  public ICommand CloseCommand
  {
    get
    {
      return (ICommand) new MvxAsyncCommand(new Func<Task>(this.OnCloseAsync), (Func<bool>) (() => !this.IsBusy));
    }
  }

    public virtual Task<bool> OnCloseAsync() => this.NavigationService.Close((IMvxViewModel)this);

    public TaskCompletionSource<object> CloseCompletionSource { get; set; }

  public override void ViewDestroy()
  {
    if (this.CloseCompletionSource != null && !this.CloseCompletionSource.Task.IsCompleted && !this.CloseCompletionSource.Task.IsFaulted)
      this.CloseCompletionSource?.TrySetCanceled();
    base.ViewDestroy();
  }

    // Добавляем кэш на уровне класса
    private PropertyInfo[] _commandProperties;

    protected void ForceCommandCanExecuteChanges(object sender, PropertyChangedEventArgs e)
    {
        // 1. Собираем команды через рефлексию только ОДИН раз
        if (_commandProperties == null)
        {
            _commandProperties = this.GetType()
                .GetRuntimeProperties()
                .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
                .ToArray();
        }

        // 2. Мгновенно пробегаемся по кэшу без нагрузки на процессор
        foreach (var propertyInfo in _commandProperties)
        {
            if (propertyInfo.GetValue(this) is IMvxCommand mvxCommand)
            {
                mvxCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IMvxLanguageBinder TextSource
    {
        get
        {
            // Возвращаем null, чтобы отключить старый механизм MvvmCross.
            // Мы теперь полагаемся исключительно на наш LocalizationManager.
            return null;
        }
    }

    public string this[string textName, params object[] args]
    {
        get
        {
            try
            {
                // ОБРАЩАЕМСЯ НАПРАВЛЕНИЯ К ТВОЕМУ МЕНЕДЖЕРУ!
                string text = Mermer.Mvvm.Tools.LocalizationManager.Instance.Get(textName, args);

                // Если перевода нет (вернулся сам ключ), рисуем решетку, как было в подлиннике
                if (text == textName)
                {
                    return args != null && args.Length > 0 ? string.Format("#" + textName, args) : "#" + textName;
                }

                return text;
            }
            catch
            {
                // Защита от битых аргументов форматирования
                return string.Format("#" + textName, args);
            }
        }
    }

    public virtual void Dispose()
  {
    string str = this["SourceUpdateTrigger-jnfdh762bkjsd864bhsd56s52", Array.Empty<object>()];
  }
}
