using Humanizer;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;
using MvvmCross.Platform.Exceptions;
using MvvmCross.Wpf.Views;
using Mermer.Ui.Pc.ViewModels;
using Mermer.Ui.Pc.Views.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Mermer.Ui.Pc;

public class ViewsContainer : MvxWpfViewsContainer
{
    // КЭШИРУЕМ ТИПЫ ОДИН РАЗ НА ВСЁ ПРИЛОЖЕНИЕ (Убирает 1 секунду фриза)
    private static readonly Lazy<List<Type>> _viewTypesCache = new Lazy<List<Type>>(() =>
        typeof(ViewsContainer).Assembly.GetTypes().Where(t => t.Name.EndsWith("View")).ToList()
    );

    // КЭШИРУЕМ РЕЗУЛЬТАТ ПОИСКА СВЯЗКИ ViewModel -> View
    private static readonly Dictionary<Type, Type> _viewModelToViewCache = new Dictionary<Type, Type>();

    public override FrameworkElement CreateView(MvxViewModelRequest request)
    {
        Type viewType = this.GetViewType(request.ViewModelType);

        if (viewType == null)
            throw new MvxException("View Type not found for " + request.ViewModelType?.ToString());

        // Activator.CreateInstance - это всё равно медленно, но MvvmCross требует этого.
        object obj = Activator.CreateInstance(viewType);

        if (obj == null)
            throw new MvxException("View not loaded for " + viewType.ToString());
        if (!(obj is IMvxWpfView mvxWpfView))
            throw new MvxException("Loaded View does not have IMvxWpfView interface " + viewType.ToString());
        if (!(obj is FrameworkElement view))
            throw new MvxException("Loaded View is not a FrameworkElement " + viewType.ToString());

        if (request is MvxViewModelInstanceRequest modelInstanceRequest)
        {
            mvxWpfView.ViewModel = modelInstanceRequest.ViewModelInstance;
            return view;
        }

        IMvxViewModelLoader mvxViewModelLoader = Mvx.Resolve<IMvxViewModelLoader>();
        mvxWpfView.ViewModel = mvxViewModelLoader.LoadViewModel(request, null);
        return view;
    }

    protected new Type GetViewType(Type viewModelType)
    {
        // Мгновенный возврат из кэша
        if (_viewModelToViewCache.TryGetValue(viewModelType, out var cachedType))
        {
            return cachedType;
        }

        Type type1 = null;
        if (viewModelType.IsGenericType)
        {
            Type genericTypeDefinition = viewModelType.GetGenericTypeDefinition();
            string genericSuffix = genericTypeDefinition?.Name.Replace($"ViewModel`{genericTypeDefinition.GetGenericArguments().Length}", "View");
            string viewName = viewModelType.GetGenericArguments()[0].Name;

            // Ищем в закэшированном списке, а не через Assembly.GetTypes()
            type1 = _viewTypesCache.Value.FirstOrDefault(t => t.Name == viewName + genericSuffix || t.Name == viewName.Pluralize() + genericSuffix);
        }
        else if (viewModelType == typeof(ReportsListViewModel))
        {
            type1 = typeof(ReportsListView);
        }

        Type resultType = type1 ?? base.GetViewType(viewModelType);

        // Сохраняем в кэш
        if (resultType != null)
        {
            _viewModelToViewCache[viewModelType] = resultType;
        }

        return resultType;
    }
}