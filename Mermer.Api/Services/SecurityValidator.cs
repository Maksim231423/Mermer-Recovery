using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres;

namespace Mermer.Api.Endpoints;

public static class SecurityValidator
{
    public static async Task<IResult?> ValidateAccountAccessAsync(
        MermerDbContext db,
        Guid? userId,
        Guid? accountId,
        string accountName = "складу/кассе")
    {
        if (!userId.HasValue || !accountId.HasValue)
            return null; // Пропускаем, если параметры не переданы

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user == null || user.IsAdmin)
            return null; // Администраторам разрешено всё

        string key = accountId.Value.ToString();
        if (user.AccountPrivileges != null && user.AccountPrivileges.TryGetValue(key, out int accessLevel))
        {
            // 0 = NoAccess (Запрещено)
            if (accessLevel == 0)
            {
                return Results.Problem(
                    detail: $"Access denied: user '{user.Username}' restricted access to treatment {accountName}.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return null;
    }
}