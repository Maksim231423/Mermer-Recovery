using System;
using System.Reflection;
using Mermer.Authorization.Enums;
using Mermer.Authorization.Services;

namespace Mermer.Ui.Core.Helpers;

public static class AccountSecurityHelper
{
    /// <summary>
    /// Проверка права на чтение (для таблиц, списков и отчетов)
    /// </summary>
    public static bool CanRead(ILoginService loginService, string accountId)
    {
        if (string.IsNullOrEmpty(accountId) || loginService?.Session == null || loginService.Session.IsAdmin)
            return true;

        if (loginService.Session.Accounts != null && loginService.Session.Accounts.TryGetValue(accountId, out var level))
        {
            // Доступно для чтения только если явно установлен флаг Read и нет No Access
            return level != AccountAccessLevel.None && level.HasFlag(AccountAccessLevel.Read);
        }

        return true;
    }

    /// <summary>
    /// Проверка права на операции (для выпадающих списков при создании, проведении и сохранении)
    /// </summary>
    public static bool CanOperate(ILoginService loginService, string accountId)
    {
        if (string.IsNullOrEmpty(accountId) || loginService?.Session == null || loginService.Session.IsAdmin)
            return true;

        if (loginService.Session.Accounts != null && loginService.Session.Accounts.TryGetValue(accountId, out var level))
        {
            // Строго проверяем наличие флага Operate
            return level != AccountAccessLevel.None && level.HasFlag(AccountAccessLevel.Operate);
        }

        return true;
    }

    /// <summary>
    /// Универсальная проверка строки таблицы/документа на право чтения для текущей сессии
    /// </summary>
    public static bool CanReadTransaction(ILoginService loginService, object transaction)
    {
        if (transaction == null || loginService?.Session == null || loginService.Session.IsAdmin)
            return true;

        var accounts = loginService.Session.Accounts;
        if (accounts == null || accounts.Count == 0)
            return true;

        var type = transaction.GetType();

        // 1. Проверка склада (WarehouseId)
        var whProp = type.GetProperty("WarehouseId");
        if (whProp != null)
        {
            var whId = whProp.GetValue(transaction) as string;
            if (!string.IsNullOrEmpty(whId) && !CanRead(loginService, whId))
                return false;
        }

        // 2. Склады перемещений (SourceWarehouseId / DestinationWarehouseId)
        var srcWhProp = type.GetProperty("SourceWarehouseId");
        if (srcWhProp != null)
        {
            var srcId = srcWhProp.GetValue(transaction) as string;
            if (!string.IsNullOrEmpty(srcId) && !CanRead(loginService, srcId))
                return false;
        }

        var dstWhProp = type.GetProperty("DestinationWarehouseId");
        if (dstWhProp != null)
        {
            var dstId = dstWhProp.GetValue(transaction) as string;
            if (!string.IsNullOrEmpty(dstId) && !CanRead(loginService, dstId))
                return false;
        }

        // 3. Проверка кассы (DepositoryId)
        var depProp = type.GetProperty("DepositoryId");
        if (depProp != null)
        {
            var depId = depProp.GetValue(transaction) as string;
            if (!string.IsNullOrEmpty(depId) && !CanRead(loginService, depId))
                return false;
        }

        // 4. Касса перевода (DestinationDepositoryId)
        var dstDepProp = type.GetProperty("DestinationDepositoryId");
        if (dstDepProp != null)
        {
            var dstDepId = dstDepProp.GetValue(transaction) as string;
            if (!string.IsNullOrEmpty(dstDepId) && !CanRead(loginService, dstDepId))
                return false;
        }

        // 5. Проверка филиала/офиса (OfficeId)
        var offProp = type.GetProperty("OfficeId");
        if (offProp != null)
        {
            var offId = offProp.GetValue(transaction) as string;
            if (!string.IsNullOrEmpty(offId) && !CanRead(loginService, offId))
                return false;
        }

        return true;
    }
}