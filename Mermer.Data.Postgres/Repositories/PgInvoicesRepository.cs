using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Mermer.Data.Postgres.Abstractions;
using Mermer.Data.Postgres.Entities;
using Mermer.Data.Postgres.Models;
using Mermer.Data.Postgres.Reports;

namespace Mermer.Data.Postgres.Repositories;

public class PgInvoicesRepository : IInvoicesRepository
{
    private readonly MermerDbContext _db;
    private readonly string _connectionString;

    public PgInvoicesRepository(MermerDbContext db, string connectionString)
    {
        _db = db;
        _connectionString = connectionString;
    }

    public async Task<Invoice?> GetAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return null;

        var entity = await _db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Discounts)
            .Include(i => i.Payments)
            .Include(i => i.Overheads)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == guid, ct);

        return entity == null ? null : MapToModel(entity);
    }

    public async Task<IReadOnlyList<InvoiceInfo>> GetInfoAsync(
        DateTime from, DateTime till,
        string? displayCurrencyId = null,
        CancellationToken ct = default)
    {
        var safeFrom = ToSafeUtcFrom(from);
        var safeTill = ToSafeUtcTill(till);
        Guid? targetCurrencyGuid = Guid.TryParse(displayCurrencyId, out var dg) ? dg : null;

        // Получаем ID базовой валюты системы (USD)
        var defCur = await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault && !c.IsDisabled, ct)
                     ?? await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(ct);
        Guid defaultCurrencyId = defCur?.Id ?? Guid.Empty;

        const string sql = """
        WITH def_cur AS (
            SELECT @defCurId::uuid AS id
        ),
        target_invoices AS (
            SELECT 
                i.id, i.code, i.date, i.invoice_type, i.is_completed, i.is_disabled,
                i.partner_id, i.warehouse_id, i.office_id, i.depository_id,
                i.user_id, i.user_name, i.group_name AS "group", i.tags,
                i.display_currency_id
            FROM invoices i
            WHERE i.date >= @from AND i.date < @till
              AND i.is_disabled = false
            ORDER BY i.date DESC
            LIMIT 100000
        ),
        invoices_with_rates AS (
            SELECT 
                ti.*,
                -- Коэффициент перевода из валюты накладной в базовую USD
                CASE 
                    WHEN ti.display_currency_id IS NULL OR ti.display_currency_id = (SELECT id FROM def_cur) THEN 1.0
                    ELSE COALESCE(cr.multiplier / NULLIF(cr.divider, 0), 1.0)
                END AS to_usd_rate,
                -- Коэффициент перевода из USD в выбранную displayCurrency (если задана)
                CASE 
                    WHEN @targetCurrency::uuid IS NULL OR @targetCurrency::uuid = (SELECT id FROM def_cur) THEN 1.0
                    ELSE COALESCE(tcr.divider / NULLIF(tcr.multiplier, 0), 1.0)
                END AS to_target_rate
            FROM target_invoices ti
            LEFT JOIN LATERAL (
                SELECT r.multiplier, r.divider 
                FROM currency_rates r 
                WHERE r.currency_id = ti.display_currency_id AND r.valid_from <= ti.date 
                ORDER BY r.valid_from DESC LIMIT 1
            ) cr ON true
            LEFT JOIN LATERAL (
                SELECT r.multiplier, r.divider 
                FROM currency_rates r 
                WHERE r.currency_id = @targetCurrency::uuid AND r.valid_from <= ti.date 
                ORDER BY r.valid_from DESC LIMIT 1
            ) tcr ON true
        ),
        lines_agg AS (
            SELECT 
                il.invoice_id,
                COALESCE(SUM(il.quantity * (il.price * (iwr.to_usd_rate * iwr.to_target_rate))), 0)::numeric(18,4) AS subtotal
            FROM invoice_lines il
            JOIN invoices_with_rates iwr ON iwr.id = il.invoice_id
            GROUP BY il.invoice_id
        ),
        discounts_agg AS (
            SELECT
                id2.invoice_id,
                COALESCE(SUM(
                    CASE id2.discount_type
                        WHEN 'Percentage' THEN COALESCE(la.subtotal, 0) * id2.amount / 100
                        ELSE (id2.amount * (iwr.to_usd_rate * iwr.to_target_rate))
                    END
                ), 0)::numeric(18,4) AS discount_total
            FROM invoice_discounts id2
            JOIN invoices_with_rates iwr ON iwr.id = id2.invoice_id
            LEFT JOIN lines_agg la ON la.invoice_id = id2.invoice_id
            GROUP BY id2.invoice_id
        ),
        payments_agg AS (
            SELECT 
                ip.invoice_id,
                COALESCE(SUM((ip.amount * (iwr.to_usd_rate * iwr.to_target_rate))) FILTER (WHERE ip.payment_type = 'Payment'), 0)::numeric(18,4) AS payment_total,
                COALESCE(SUM((ip.amount * (iwr.to_usd_rate * iwr.to_target_rate))) FILTER (WHERE ip.payment_type = 'Change'),  0)::numeric(18,4) AS change_total
            FROM invoice_payments ip
            JOIN invoices_with_rates iwr ON iwr.id = ip.invoice_id
            GROUP BY ip.invoice_id
        ),
        overheads_agg AS (
            SELECT 
                io.invoice_id,
                COALESCE(SUM((io.amount * (iwr.to_usd_rate * iwr.to_target_rate))), 0)::numeric(18,4) AS overhead_total
            FROM invoice_overheads io
            JOIN invoices_with_rates iwr ON iwr.id = io.invoice_id
            GROUP BY io.invoice_id
        )
        SELECT
            iwr.id, iwr.code, iwr.date, iwr.invoice_type, iwr.is_completed, iwr.is_disabled,
            iwr.partner_id, p.name AS partner_name,
            iwr.warehouse_id, w.name AS warehouse_name,
            iwr.office_id, o.name AS office_name,
            iwr.depository_id, iwr.user_id, iwr.user_name,
            iwr."group", iwr.tags,

            COALESCE(la.subtotal, 0)::numeric(18,4) AS subtotal,
            COALESCE(da.discount_total, 0)::numeric(18,4) AS discounts_total,
            COALESCE(oa.overhead_total, 0)::numeric(18,4) AS overheads_total,

            (COALESCE(la.subtotal, 0)
             - COALESCE(da.discount_total, 0)
             + COALESCE(oa.overhead_total, 0))::numeric(18,4) AS grand_total,

            COALESCE(pa.payment_total, 0)::numeric(18,4) AS payments_total,

            GREATEST(
                0,
                COALESCE(la.subtotal, 0)
                - COALESCE(da.discount_total, 0)
                + COALESCE(oa.overhead_total, 0)
                - COALESCE(pa.payment_total, 0)
                + COALESCE(pa.change_total,  0)
            )::numeric(18,4) AS left_total
        FROM invoices_with_rates iwr
        LEFT JOIN partners     p  ON p.id  = iwr.partner_id
        LEFT JOIN warehouses   w  ON w.id  = iwr.warehouse_id
        LEFT JOIN offices      o  ON o.id  = iwr.office_id
        LEFT JOIN lines_agg     la ON la.invoice_id = iwr.id
        LEFT JOIN discounts_agg da ON da.invoice_id = iwr.id
        LEFT JOIN payments_agg  pa ON pa.invoice_id = iwr.id
        LEFT JOIN overheads_agg oa ON oa.invoice_id = iwr.id
        ORDER BY iwr.date DESC;
        """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql,
            new { from = safeFrom, till = safeTill, defCurId = defaultCurrencyId, targetCurrency = targetCurrencyGuid },
            cancellationToken: ct));

        return rows.Select(r => new InvoiceInfo
        {
            Id = ((Guid)r.id).ToString(),
            Code = (string?)r.code,
            Date = (DateTime)r.date,
            InvoiceType = Enum.Parse<InvoiceType>((string)r.invoice_type),
            IsCompleted = (bool)r.is_completed,
            IsDisabled = (bool)r.is_disabled,
            PartnerId = ((Guid?)r.partner_id)?.ToString(),
            PartnerName = (string?)r.partner_name,
            WarehouseId = ((Guid?)r.warehouse_id)?.ToString(),
            WarehouseName = (string?)r.warehouse_name,
            OfficeId = ((Guid?)r.office_id)?.ToString(),
            OfficeName = (string?)r.office_name,
            DepositoryId = ((Guid?)r.depository_id)?.ToString(),
            UserId = ((Guid?)r.user_id)?.ToString(),
            UserName = (string?)r.user_name,
            Group = (string?)r.group,
            Tags = r.tags is string[] tagsArray ? tagsArray.ToList() : new List<string>(),
            Subtotal = (decimal)r.subtotal,
            DiscountsTotal = (decimal)r.discounts_total,
            OverheadsTotal = (decimal)r.overheads_total,
            GrandTotal = (decimal)r.grand_total,
            PaymentsTotal = (decimal)r.payments_total,
            LeftTotal = (decimal)r.left_total
        }).ToList();
    }

    public async Task<int> CountInfoAsync(DateTime from, DateTime till, CancellationToken ct = default)
    {
        var safeFrom = ToSafeUtcFrom(from);
        var safeTill = ToSafeUtcTill(till);

        return await _db.Invoices
            .Where(i => i.Date >= safeFrom && i.Date < safeTill && !i.IsDisabled)
            .CountAsync(ct);
    }

    private static DateTime ToSafeUtcFrom(DateTime dt)
    {
        if (dt == default || dt == DateTime.MinValue)
            return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
    }

    private static DateTime ToSafeUtcTill(DateTime dt)
    {
        if (dt == default || dt >= DateTime.MaxValue.AddDays(-2))
            return new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

        // Если клиент прислал строго полночь без времени (00:00:00.000), 
        // значит это дата-фильтр по одному дню, и нужно взять до конца дня (+1 день).
        // Если же клиент передал время (например 21:00:00Z или 23:59:59Z), 
        // значит клиент УЖЕ сам рассчитал точную границу — сдвигать НЕЛЬЗЯ!
        if (dt.TimeOfDay == TimeSpan.Zero && dt.Kind != DateTimeKind.Utc)
        {
            return utc.AddDays(1);
        }

        return utc;
    }

    public async Task<IReadOnlyList<InvoicePaymentInfo>> GetPaymentInfoAsync(
        DateTime from, DateTime till, string? officeId, string? partnerId,
        string? displayCurrencyId = null,
        CancellationToken ct = default)
    {
        Guid? officeGuid = Guid.TryParse(officeId, out var og) ? og : null;
        Guid? partnerGuid = Guid.TryParse(partnerId, out var pg) ? pg : null;
        Guid? displayGuid = Guid.TryParse(displayCurrencyId, out var dg) ? dg : null;

        var safeFrom = ToSafeUtcFrom(from);
        var safeTill = ToSafeUtcTill(till);

        const string sql = """
        WITH conv AS (
            SELECT invoice_id, currency_id, multiplier, divider
            FROM invoice_currency_convertions
        ),
        lines_agg AS (
            SELECT il.invoice_id,
                   COALESCE(SUM(
                       il.quantity * il.price
                       * COALESCE(c.multiplier / NULLIF(c.divider, 0), 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / NULLIF(d.multiplier, 0), 1)
                               ELSE 1 END)
                   ), 0)::numeric(18,4) AS subtotal
            FROM invoice_lines il
            LEFT JOIN conv c ON c.invoice_id = il.invoice_id AND c.currency_id = il.currency_id
            LEFT JOIN conv d ON d.invoice_id = il.invoice_id AND d.currency_id = @displayCurrencyId::uuid
            GROUP BY il.invoice_id
        ),
        discounts_agg AS (
            SELECT
                id2.invoice_id,
                COALESCE(SUM(
                    CASE id2.discount_type
                        WHEN 'Percentage' THEN COALESCE(la.subtotal,0) * id2.amount / 100
                        ELSE id2.amount
                          * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                                  THEN COALESCE(d.divider / NULLIF(d.multiplier, 0), 1)
                                  ELSE 1 END)
                    END
                ), 0)::numeric(18,4) AS discount_total
            FROM invoice_discounts id2
            LEFT JOIN lines_agg la ON la.invoice_id = id2.invoice_id
            LEFT JOIN conv d       ON d.invoice_id  = id2.invoice_id AND d.currency_id = @displayCurrencyId::uuid
            GROUP BY id2.invoice_id
        ),
        payments_agg AS (
            SELECT ip.invoice_id,
                   COALESCE(SUM(ip.amount
                       * COALESCE(c.multiplier / NULLIF(c.divider, 0), 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / NULLIF(d.multiplier, 0), 1)
                               ELSE 1 END)
                   ) FILTER (WHERE ip.payment_type = 'Payment'), 0)::numeric(18,4) AS payment_total,
                   COALESCE(SUM(ip.amount
                       * COALESCE(c.multiplier / NULLIF(c.divider, 0), 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / NULLIF(d.multiplier, 0), 1)
                               ELSE 1 END)
                   ) FILTER (WHERE ip.payment_type = 'Change'),  0)::numeric(18,4) AS change_total
            FROM invoice_payments ip
            LEFT JOIN conv c ON c.invoice_id = ip.invoice_id AND c.currency_id = ip.currency_id
            LEFT JOIN conv d ON d.invoice_id = ip.invoice_id AND d.currency_id = @displayCurrencyId::uuid
            GROUP BY ip.invoice_id
        ),
        overheads_agg AS (
            SELECT io.invoice_id,
                   COALESCE(SUM(io.amount
                       * COALESCE(c.multiplier / NULLIF(c.divider, 0), 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / NULLIF(d.multiplier, 0), 1)
                               ELSE 1 END)
                   ), 0)::numeric(18,4) AS overhead_total
            FROM invoice_overheads io
            LEFT JOIN conv c ON c.invoice_id = io.invoice_id AND c.currency_id = io.currency_id
            LEFT JOIN conv d ON d.invoice_id = io.invoice_id AND d.currency_id = @displayCurrencyId::uuid
            GROUP BY io.invoice_id
        )
        SELECT
            i.id,
            i.code,
            i.date,
            i.due_date,
            i.user_name,
            i.office_id,
            i.group_name AS "group",
            i.tags,
            i.invoice_type,
            i.is_completed,
            i.partner_id,
            p.name                                                              AS partner_name,

            (COALESCE(la.subtotal, 0)
             - COALESCE(da.discount_total, 0)
             + COALESCE(oa.overhead_total, 0))::numeric(18,4)                   AS grand_total,

            COALESCE(pa.payment_total, 0)::numeric(18,4)                        AS payments_total,
            COALESCE(pa.change_total,  0)::numeric(18,4)                        AS changes_total,

            COALESCE(act.debit_total,  0)::numeric(18,4)                        AS partner_debit,
            COALESCE(act.credit_total, 0)::numeric(18,4)                        AS partner_credit,
            act.last_payment_date
        FROM invoices i
        LEFT JOIN partners      p   ON p.id  = i.partner_id
        LEFT JOIN lines_agg     la  ON la.invoice_id = i.id
        LEFT JOIN discounts_agg da  ON da.invoice_id = i.id
        LEFT JOIN payments_agg  pa  ON pa.invoice_id = i.id
        LEFT JOIN overheads_agg oa  ON oa.invoice_id = i.id
        LEFT JOIN LATERAL (
            SELECT
                0::numeric(18,4) AS debit_total,
                0::numeric(18,4) AS credit_total,
                (
                    SELECT MAX(fs.date)
                    FROM funds_slips fs
                    WHERE fs.partner_id = i.partner_id
                      AND fs.is_disabled = false
                      AND (i.office_id IS NULL OR fs.office_id = i.office_id)
                ) AS last_payment_date
        ) act ON true
        WHERE i.date >= @from AND i.date < @till
          AND i.is_disabled = false
          AND (@officeId  IS NULL OR i.office_id  = @officeId)
          AND (@partnerId IS NULL OR i.partner_id = @partnerId)
        ORDER BY i.date DESC
        """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            from = safeFrom,
            till = safeTill,
            officeId = officeGuid,
            partnerId = partnerGuid,
            displayCurrencyId = displayGuid
        }, cancellationToken: ct));

        return rows.Select(r => new InvoicePaymentInfo
        {
            Id = ((Guid)r.id).ToString(),
            Code = (string?)r.code,
            Date = (DateTime)r.date,
            DueDate = (DateTime?)r.due_date,
            UserName = (string?)r.user_name,
            OfficeId = ((Guid?)r.office_id)?.ToString(),
            InvoiceType = Enum.Parse<InvoiceType>((string)r.invoice_type),
            IsCompleted = (bool)r.is_completed,
            PartnerId = ((Guid?)r.partner_id)?.ToString(),
            PartnerName = (string?)r.partner_name,
            Total = (decimal)r.grand_total,
            PaymentsTotal = (decimal)r.payments_total,
            ChangesTotal = (decimal)r.changes_total,
            PartnerDebit = (decimal)r.partner_debit,
            PartnerCredit = (decimal)r.partner_credit,
            LastPaymentDate = (DateTime?)r.last_payment_date
        }).ToList();
    }

    public async Task<int> CountPaymentInfoAsync(
        DateTime from, DateTime till, string? officeId, string? partnerId, CancellationToken ct = default)
    {
        Guid? officeGuid = Guid.TryParse(officeId, out var og) ? og : null;
        Guid? partnerGuid = Guid.TryParse(partnerId, out var pg) ? pg : null;

        var safeFrom = ToSafeUtcFrom(from);
        var safeTill = ToSafeUtcTill(till);

        return await _db.Invoices
            .Where(i => i.Date >= safeFrom && i.Date < safeTill
                     && !i.IsDisabled
                     && (officeGuid == null || i.OfficeId == officeGuid)
                     && (partnerGuid == null || i.PartnerId == partnerGuid))
            .CountAsync(ct);
    }

    public async Task<Invoice> CreateAsync(Invoice model, CancellationToken ct = default)
    {
        model.Id ??= Guid.NewGuid().ToString();
        var entity = MapToEntity(model);

        Guid invoiceId = entity.Id;
        foreach (var l in model.Lines ?? Enumerable.Empty<InvoiceLine>()) entity.Lines.Add(MapLineToEntity(l, invoiceId));
        foreach (var d in model.Discounts ?? Enumerable.Empty<InvoiceDiscount>()) entity.Discounts.Add(MapDiscountToEntity(d, invoiceId));
        foreach (var p in model.Payments ?? Enumerable.Empty<InvoicePayment>()) entity.Payments.Add(MapPaymentToEntity(p, invoiceId, "Payment"));
        foreach (var c in model.Changes ?? Enumerable.Empty<InvoicePayment>()) entity.Payments.Add(MapPaymentToEntity(c, invoiceId, "Change"));
        foreach (var o in model.Overheads ?? Enumerable.Empty<InvoiceOverhead>()) entity.Overheads.Add(MapOverheadToEntity(o, invoiceId));

        _db.Invoices.Add(entity);
        await _db.SaveChangesAsync(ct);
        return model;
    }

    public async Task<Invoice> UpdateAsync(Invoice model, CancellationToken ct = default)
    {
        if (!Guid.TryParse(model.Id, out var guid))
            throw new ArgumentException("Invalid invoice ID", nameof(model));

        var entity = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == guid, ct);
        if (entity == null)
        {
            return await CreateAsync(model, ct);
        }

        // 1. Обновляем реквизиты шапки
        entity.Code = model.Code;
        entity.Date = model.Date.ToUniversalTime();
        entity.DueDate = model.DueDate?.ToUniversalTime();
        entity.InvoiceType = model.InvoiceType.ToString();
        entity.IsCompleted = model.IsCompleted;
        entity.IsDisabled = model.IsDisabled;
        entity.StockPriceGroup = model.StockPriceGroup;
        entity.DebitCreditLeftAmount = model.DebitCreditLeftAmount;
        entity.Description = model.Description;
        entity.Group = model.Group;
        entity.Tags = model.Tags != null && model.Tags.Count > 0 ? model.Tags.ToArray() : Array.Empty<string>();
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        if (Guid.TryParse(model.OfficeId, out var officeId)) entity.OfficeId = officeId == Guid.Empty ? null : officeId;
        if (Guid.TryParse(model.WarehouseId, out var warehouseId)) entity.WarehouseId = warehouseId == Guid.Empty ? null : warehouseId;
        if (Guid.TryParse(model.DepositoryId, out var depositoryId)) entity.DepositoryId = depositoryId == Guid.Empty ? null : depositoryId;
        if (Guid.TryParse(model.PartnerId, out var partnerId)) entity.PartnerId = partnerId == Guid.Empty ? null : partnerId;
        if (Guid.TryParse(model.DisplayCurrencyId, out var dispCur)) entity.DisplayCurrencyId = dispCur == Guid.Empty ? null : dispCur;
        if (Guid.TryParse(model.UserId, out var userId)) entity.UserId = userId == Guid.Empty ? null : userId;
        entity.UserName = model.UserName;

        // 2. Удаляем старые связанные данные в базе через прямые запросы
        await _db.InvoiceLines.Where(x => x.InvoiceId == guid).ExecuteDeleteAsync(ct);
        await _db.InvoiceDiscounts.Where(x => x.InvoiceId == guid).ExecuteDeleteAsync(ct);
        await _db.InvoicePayments.Where(x => x.InvoiceId == guid).ExecuteDeleteAsync(ct);
        await _db.InvoiceOverheads.Where(x => x.InvoiceId == guid).ExecuteDeleteAsync(ct);

        // 3. Сбрасываем отслеживание дочерних сущностей в памяти ChangeTracker
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is InvoiceLineEntity ||
                entry.Entity is InvoiceDiscountEntity ||
                entry.Entity is InvoicePaymentEntity ||
                entry.Entity is InvoiceOverheadEntity)
            {
                entry.State = EntityState.Detached;
            }
        }

        // 4. Добавляем новые коллекции с чистыми идентификаторами
        if (model.Lines != null && model.Lines.Count > 0)
        {
            var newLines = model.Lines.Select(l => MapLineToEntity(l, guid)).ToList();
            await _db.InvoiceLines.AddRangeAsync(newLines, ct);
        }

        if (model.Discounts != null && model.Discounts.Count > 0)
        {
            var newDiscounts = model.Discounts.Select(d => MapDiscountToEntity(d, guid)).ToList();
            await _db.InvoiceDiscounts.AddRangeAsync(newDiscounts, ct);
        }

        var newPayments = new List<InvoicePaymentEntity>();
        if (model.Payments != null && model.Payments.Count > 0)
            newPayments.AddRange(model.Payments.Select(p => MapPaymentToEntity(p, guid, "Payment")));
        if (model.Changes != null && model.Changes.Count > 0)
            newPayments.AddRange(model.Changes.Select(c => MapPaymentToEntity(c, guid, "Change")));

        if (newPayments.Count > 0)
        {
            await _db.InvoicePayments.AddRangeAsync(newPayments, ct);
        }

        if (model.Overheads != null && model.Overheads.Count > 0)
        {
            var newOverheads = model.Overheads.Select(o => MapOverheadToEntity(o, guid)).ToList();
            await _db.InvoiceOverheads.AddRangeAsync(newOverheads, ct);
        }

        await _db.SaveChangesAsync(ct);
        return model;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            return;
        var entity = await _db.Invoices.FindAsync(new object[] { guid }, ct);
        if (entity != null)
        {
            entity.IsDisabled = true;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<RevenueReportRow>> GetRevenueReportAsync(
        DateTime from, DateTime till, string? warehouseId = null, CancellationToken ct = default)
    {
        Guid? warehouseGuid = Guid.TryParse(warehouseId, out var wg) ? wg : null;

        var safeTill = till >= DateTime.MaxValue.AddDays(-2) ? new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) : till.AddDays(1).ToUniversalTime();

        const string sql = """
            SELECT
                i.date                                                       AS date,
                i.id::text                                                   AS invoice_id,
                i.code                                                       AS invoice_code,
                i.invoice_type                                               AS invoice_type,

                il.id::text                                                  AS line_id,
                il.source_id::text                                           AS source_line_id,
                il.stock_id::text                                            AS stock_id,
                s.code                                                       AS stock_code,
                s.name                                                       AS stock_name,

                i.warehouse_id::text                                         AS warehouse_id,
                w.name                                                       AS warehouse_name,

                il.quantity                                                  AS quantity,
                il.price                                                     AS unit_price
            FROM invoices i
            JOIN invoice_lines il ON il.invoice_id = i.id
            LEFT JOIN stocks      s ON s.id = il.stock_id
            LEFT JOIN warehouses w ON w.id = i.warehouse_id
            WHERE i.is_completed = true
              AND i.is_disabled  = false
              AND i.date <= @till
              AND (@warehouseId::uuid IS NULL OR i.warehouse_id = @warehouseId)
            ORDER BY i.date ASC, il.id ASC
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, new
        {
            till = safeTill,
            warehouseId = warehouseGuid
        }, cancellationToken: ct));

        var movements = rows.Select(r => new RevenueCalculator.StockMovement
        {
            Date = (DateTime)r.date,
            InvoiceId = (string)r.invoice_id,
            InvoiceCode = (string?)r.invoice_code,
            InvoiceType = Enum.Parse<InvoiceType>((string)r.invoice_type),
            LineId = (string)r.line_id,
            SourceLineId = (string?)r.source_line_id,
            StockId = (string?)r.stock_id,
            StockCode = (string?)r.stock_code,
            StockName = (string?)r.stock_name,
            WarehouseId = (string?)r.warehouse_id,
            WarehouseName = (string?)r.warehouse_name,
            Quantity = (decimal)r.quantity,
            UnitPrice = (decimal)r.unit_price
        });

        return RevenueCalculator.Build(movements, from, till);
    }

    private static Invoice MapToModel(InvoiceEntity e)
    {
        return new Invoice
        {
            Id = e.Id.ToString(),
            Code = e.Code,
            Date = e.Date.UtcDateTime,
            DueDate = e.DueDate?.UtcDateTime,
            InvoiceType = Enum.Parse<InvoiceType>(e.InvoiceType),
            IsCompleted = e.IsCompleted,
            IsDisabled = e.IsDisabled,
            OfficeId = e.OfficeId?.ToString(),
            WarehouseId = e.WarehouseId?.ToString(),
            DepositoryId = e.DepositoryId?.ToString(),
            PartnerId = e.PartnerId?.ToString(),
            DisplayCurrencyId = e.DisplayCurrencyId?.ToString(),
            StockPriceGroup = e.StockPriceGroup,
            DebitCreditLeftAmount = e.DebitCreditLeftAmount,
            Description = e.Description,
            UserId = e.UserId?.ToString(),
            UserName = e.UserName,
            Group = e.Group,
            Tags = e.Tags != null ? e.Tags.ToList() : new List<string>(),
            Lines = e.Lines.Select(l => new InvoiceLine
            {
                Id = l.Id.ToString(),
                SourceId = l.SourceId?.ToString(),
                StockId = l.StockId?.ToString(),
                UnitId = l.UnitId?.ToString(),
                Quantity = l.Quantity,
                Price = l.Price,
                CurrencyId = l.CurrencyId?.ToString(),
                SortOrder = l.SortOrder
            }).ToList(),
            Discounts = e.Discounts.Select(d => new InvoiceDiscount
            {
                Id = d.Id.ToString(),
                Type = d.DiscountType == "Percentage" ? InvoiceDiscountType.Percentage : InvoiceDiscountType.Flat,
                Amount = d.Amount,
                Description = d.Description,
                SortOrder = d.SortOrder
            }).ToList(),
            Payments = e.Payments
                .Where(p => p.PaymentType == "Payment")
                .Select(p => new InvoicePayment
                {
                    Id = p.Id.ToString(),
                    Amount = p.Amount,
                    CurrencyId = p.CurrencyId?.ToString(),
                    SortOrder = p.SortOrder
                }).ToList(),
            Changes = e.Payments
                .Where(p => p.PaymentType == "Change")
                .Select(p => new InvoicePayment
                {
                    Id = p.Id.ToString(),
                    Amount = p.Amount,
                    CurrencyId = p.CurrencyId?.ToString(),
                    SortOrder = p.SortOrder
                }).ToList(),
            Overheads = e.Overheads.Select(o => new InvoiceOverhead
            {
                Id = o.Id.ToString(),
                Amount = o.Amount,
                CurrencyId = o.CurrencyId?.ToString(),
                Description = o.Description,
                SortOrder = o.SortOrder
            }).ToList()
        };
    }

    private static InvoiceEntity MapToEntity(Invoice m)
    {
        Guid.TryParse(m.Id, out var id);
        Guid.TryParse(m.OfficeId, out var officeId);
        Guid.TryParse(m.WarehouseId, out var warehouseId);
        Guid.TryParse(m.DepositoryId, out var depositoryId);
        Guid.TryParse(m.PartnerId, out var partnerId);
        Guid.TryParse(m.DisplayCurrencyId, out var dispCur);
        Guid.TryParse(m.UserId, out var userId);

        return new InvoiceEntity
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Code = m.Code,
            Date = m.Date.ToUniversalTime(),
            DueDate = m.DueDate?.ToUniversalTime(),
            InvoiceType = m.InvoiceType.ToString(),
            IsCompleted = m.IsCompleted,
            IsDisabled = m.IsDisabled,
            OfficeId = officeId == Guid.Empty ? null : officeId,
            WarehouseId = warehouseId == Guid.Empty ? null : warehouseId,
            DepositoryId = depositoryId == Guid.Empty ? null : depositoryId,
            PartnerId = partnerId == Guid.Empty ? null : partnerId,
            DisplayCurrencyId = dispCur == Guid.Empty ? null : dispCur,
            UserId = userId == Guid.Empty ? null : userId,
            UserName = m.UserName,
            StockPriceGroup = m.StockPriceGroup,
            DebitCreditLeftAmount = m.DebitCreditLeftAmount,
            Group = m.Group,
            Tags = m.Tags != null && m.Tags.Count > 0 ? m.Tags.ToArray() : Array.Empty<string>(),
            Description = m.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static InvoiceLineEntity MapLineToEntity(InvoiceLine l, Guid invoiceId)
    {
        Guid.TryParse(l.StockId, out var stockId);
        Guid.TryParse(l.UnitId, out var unitId);
        Guid.TryParse(l.CurrencyId, out var currencyId);
        Guid.TryParse(l.SourceId, out var sourceId);

        var lineId = (Guid.TryParse(l.Id, out var lid) && lid != Guid.Empty) ? lid : Guid.NewGuid();

        return new InvoiceLineEntity
        {
            Id = lineId,
            InvoiceId = invoiceId,
            SourceId = sourceId == Guid.Empty ? null : sourceId,
            StockId = stockId == Guid.Empty ? null : stockId,
            UnitId = unitId == Guid.Empty ? null : unitId,
            CurrencyId = currencyId == Guid.Empty ? null : currencyId,
            Quantity = l.Quantity,
            Price = l.Price,
            SortOrder = l.SortOrder
        };
    }

    private static InvoiceDiscountEntity MapDiscountToEntity(InvoiceDiscount d, Guid invoiceId)
    {
        return new InvoiceDiscountEntity
        {
            Id = Guid.TryParse(d.Id, out var did) ? did : Guid.NewGuid(),
            InvoiceId = invoiceId,
            DiscountType = d.Type == InvoiceDiscountType.Percentage ? "Percentage" : "Flat",
            Amount = d.Amount,
            Description = d.Description,
            SortOrder = d.SortOrder
        };
    }

    private static InvoicePaymentEntity MapPaymentToEntity(
        InvoicePayment p, Guid invoiceId, string paymentType)
    {
        Guid.TryParse(p.CurrencyId, out var currencyId);
        return new InvoicePaymentEntity
        {
            Id = Guid.TryParse(p.Id, out var pid) ? pid : Guid.NewGuid(),
            InvoiceId = invoiceId,
            PaymentType = paymentType,
            Amount = p.Amount,
            CurrencyId = currencyId == Guid.Empty ? null : currencyId,
            SortOrder = p.SortOrder
        };
    }

    private static InvoiceOverheadEntity MapOverheadToEntity(InvoiceOverhead o, Guid invoiceId)
    {
        Guid.TryParse(o.CurrencyId, out var currencyId);
        return new InvoiceOverheadEntity
        {
            Id = Guid.TryParse(o.Id, out var oid) ? oid : Guid.NewGuid(),
            InvoiceId = invoiceId,
            Amount = o.Amount,
            CurrencyId = currencyId == Guid.Empty ? null : currencyId,
            Description = o.Description,
            SortOrder = o.SortOrder
        };
    }
}