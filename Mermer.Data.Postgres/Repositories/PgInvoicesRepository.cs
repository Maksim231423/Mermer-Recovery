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
            .AsSplitQuery() // <-- Устраняет предупреждение 20504 MultipleCollectionIncludeWarning
            .FirstOrDefaultAsync(i => i.Id == guid, ct);

        return entity == null ? null : MapToModel(entity);
    }

    public async Task<IReadOnlyList<InvoiceInfo>> GetInfoAsync(
    DateTime from, DateTime till,
    string? displayCurrencyId = null,
    CancellationToken ct = default)
    {
        var safeFrom = from == default || from == DateTime.MinValue ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) : from.ToUniversalTime();
        var safeTill = till >= DateTime.MaxValue.AddDays(-2) ? new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) : till.AddDays(1).ToUniversalTime();

        // ОПТИМИЗАЦИЯ: Сначала отбираем только нужные ID накладных через индекс,
        // а агрегации делаем ТОЛЬКО по отобранным накладным!
        const string sql = """
        WITH target_invoices AS (
            SELECT 
                i.id, i.code, i.date, i.invoice_type, i.is_completed, i.is_disabled,
                i.partner_id, i.warehouse_id, i.office_id, i.depository_id,
                i.user_id, i.user_name, i.group_name AS "group", i.tags
            FROM invoices i
            WHERE i.date >= @from AND i.date < @till
              AND i.is_disabled = false
            ORDER BY i.date DESC
            LIMIT 500 -- Защита: не даем выгрузить больше 500 записей за раз без пагинации
        ),
        lines_agg AS (
            SELECT 
                il.invoice_id,
                COALESCE(SUM(il.quantity * il.price), 0)::numeric(18,4) AS subtotal
            FROM invoice_lines il
            JOIN target_invoices ti ON ti.id = il.invoice_id
            GROUP BY il.invoice_id
        ),
        discounts_agg AS (
            SELECT
                id2.invoice_id,
                COALESCE(SUM(
                    CASE id2.discount_type
                        WHEN 'Percentage' THEN COALESCE(la.subtotal, 0) * id2.amount / 100
                        ELSE id2.amount
                    END
                ), 0)::numeric(18,4) AS discount_total
            FROM invoice_discounts id2
            JOIN target_invoices ti ON ti.id = id2.invoice_id
            LEFT JOIN lines_agg la ON la.invoice_id = id2.invoice_id
            GROUP BY id2.invoice_id
        ),
        payments_agg AS (
            SELECT 
                ip.invoice_id,
                COALESCE(SUM(ip.amount) FILTER (WHERE ip.payment_type = 'Payment'), 0)::numeric(18,4) AS payment_total,
                COALESCE(SUM(ip.amount) FILTER (WHERE ip.payment_type = 'Change'),  0)::numeric(18,4) AS change_total
            FROM invoice_payments ip
            JOIN target_invoices ti ON ti.id = ip.invoice_id
            GROUP BY ip.invoice_id
        ),
        overheads_agg AS (
            SELECT 
                io.invoice_id,
                COALESCE(SUM(io.amount), 0)::numeric(18,4) AS overhead_total
            FROM invoice_overheads io
            JOIN target_invoices ti ON ti.id = io.invoice_id
            GROUP BY io.invoice_id
        )
        SELECT
            ti.id, ti.code, ti.date, ti.invoice_type, ti.is_completed, ti.is_disabled,
            ti.partner_id, p.name AS partner_name,
            ti.warehouse_id, w.name AS warehouse_name,
            ti.office_id, o.name AS office_name,
            ti.depository_id, ti.user_id, ti.user_name,
            ti."group", ti.tags,

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
        FROM target_invoices ti
        LEFT JOIN partners     p  ON p.id  = ti.partner_id
        LEFT JOIN warehouses   w  ON w.id  = ti.warehouse_id
        LEFT JOIN offices      o  ON o.id  = ti.office_id
        LEFT JOIN lines_agg     la ON la.invoice_id = ti.id
        LEFT JOIN discounts_agg da ON da.invoice_id = ti.id
        LEFT JOIN payments_agg  pa ON pa.invoice_id = ti.id
        LEFT JOIN overheads_agg oa ON oa.invoice_id = ti.id
        ORDER BY ti.date DESC;
        """;

        await using var conn = new NpgsqlConnection(_connectionString);
        var rows = await conn.QueryAsync(new CommandDefinition(sql,
            new { from = safeFrom, till = safeTill },
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
        var safeFrom = from == default || from == DateTime.MinValue ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) : from.ToUniversalTime();
        var safeTill = till >= DateTime.MaxValue.AddDays(-2) ? new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) : till.AddDays(1).ToUniversalTime();

        return await _db.Invoices
            .Where(i => i.Date >= safeFrom && i.Date < safeTill)
            .CountAsync(ct);
    }

    public async Task<IReadOnlyList<InvoicePaymentInfo>> GetPaymentInfoAsync(
     DateTime from, DateTime till, string? officeId, string? partnerId,
     string? displayCurrencyId = null,
     CancellationToken ct = default)
    {
        Guid? officeGuid = Guid.TryParse(officeId, out var og) ? og : null;
        Guid? partnerGuid = Guid.TryParse(partnerId, out var pg) ? pg : null;
        Guid? displayGuid = Guid.TryParse(displayCurrencyId, out var dg) ? dg : null;

        var safeFrom = from == default || from == DateTime.MinValue ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) : from.ToUniversalTime();
        var safeTill = till >= DateTime.MaxValue.AddDays(-2) ? new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) : till.AddDays(1).ToUniversalTime();

        const string sql = """
        WITH conv AS (
            SELECT invoice_id, currency_id, multiplier, divider
            FROM invoice_currency_convertions
        ),
        lines_agg AS (
            SELECT il.invoice_id,
                   COALESCE(SUM(
                       il.quantity * il.price
                       * COALESCE(c.multiplier / c.divider, 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / d.multiplier, 1)
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
                                  THEN COALESCE(d.divider / d.multiplier, 1)
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
                       * COALESCE(c.multiplier / c.divider, 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / d.multiplier, 1)
                               ELSE 1 END)
                   ) FILTER (WHERE ip.payment_type = 'Payment'), 0)::numeric(18,4) AS payment_total,
                   COALESCE(SUM(ip.amount
                       * COALESCE(c.multiplier / c.divider, 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / d.multiplier, 1)
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
                       * COALESCE(c.multiplier / c.divider, 1)
                       * (CASE WHEN @displayCurrencyId::uuid IS NOT NULL
                               THEN COALESCE(d.divider / d.multiplier, 1)
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
            p.name                                              AS partner_name,

            (COALESCE(la.subtotal, 0)
             - COALESCE(da.discount_total, 0)
             + COALESCE(oa.overhead_total, 0))::numeric(18,4)   AS grand_total,

            COALESCE(pa.payment_total, 0)::numeric(18,4)        AS payments_total,
            COALESCE(pa.change_total,  0)::numeric(18,4)        AS changes_total,

            COALESCE(act.debit_total,  0)::numeric(18,4)        AS partner_debit,
            COALESCE(act.credit_total, 0)::numeric(18,4)        AS partner_credit,
            act.last_payment_date
        FROM invoices i
        LEFT JOIN partners      p   ON p.id  = i.partner_id
        LEFT JOIN lines_agg     la  ON la.invoice_id = i.id
        LEFT JOIN discounts_agg da  ON da.invoice_id = i.id
        LEFT JOIN payments_agg  pa  ON pa.invoice_id = i.id
        LEFT JOIN overheads_agg oa  ON oa.invoice_id = i.id
        LEFT JOIN LATERAL (
            SELECT
                SUM(amount) FILTER (WHERE action_type = 'Debit')  AS debit_total,
                SUM(amount) FILTER (WHERE action_type = 'Credit') AS credit_total,
                MAX(created_at) AS last_payment_date
            FROM partner_actions
            WHERE partner_id = i.partner_id
              AND (i.office_id IS NULL OR office_id = i.office_id)
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

        var safeFrom = from == default || from == DateTime.MinValue ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) : from.ToUniversalTime();
        var safeTill = till >= DateTime.MaxValue.AddDays(-2) ? new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) : till.AddDays(1).ToUniversalTime();

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

        var entity = await _db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.Discounts)
            .Include(i => i.Payments)
            .Include(i => i.Overheads)
            .AsSplitQuery() // <-- Устраняет предупреждение 20504 MultipleCollectionIncludeWarning
            .FirstOrDefaultAsync(i => i.Id == guid, ct)
            ?? throw new InvalidOperationException($"Invoice {guid} not found");

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
        // <-- Гарантируем корректный маппинг в PostgreSQL массив
        entity.Tags = model.Tags != null && model.Tags.Count > 0 ? model.Tags.ToArray() : Array.Empty<string>();
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        entity.Lines.Clear();
        foreach (var l in model.Lines ?? Enumerable.Empty<InvoiceLine>())
            entity.Lines.Add(MapLineToEntity(l, guid));

        entity.Discounts.Clear();
        foreach (var d in model.Discounts ?? Enumerable.Empty<InvoiceDiscount>())
            entity.Discounts.Add(MapDiscountToEntity(d, guid));

        entity.Payments.Clear();
        foreach (var p in model.Payments ?? Enumerable.Empty<InvoicePayment>())
            entity.Payments.Add(MapPaymentToEntity(p, guid, "Payment"));
        foreach (var c in model.Changes ?? Enumerable.Empty<InvoicePayment>())
            entity.Payments.Add(MapPaymentToEntity(c, guid, "Change"));

        entity.Overheads.Clear();
        foreach (var o in model.Overheads ?? Enumerable.Empty<InvoiceOverhead>())
            entity.Overheads.Add(MapOverheadToEntity(o, guid));

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
                i.date                                       AS date,
                i.id::text                                   AS invoice_id,
                i.code                                       AS invoice_code,
                i.invoice_type                               AS invoice_type,

                il.id::text                                  AS line_id,
                il.source_id::text                           AS source_line_id,
                il.stock_id::text                            AS stock_id,
                s.code                                       AS stock_code,
                s.name                                       AS stock_name,

                i.warehouse_id::text                         AS warehouse_id,
                w.name                                       AS warehouse_name,

                il.quantity                                  AS quantity,
                il.price                                     AS unit_price
            FROM invoices i
            JOIN invoice_lines il ON il.invoice_id = i.id
            LEFT JOIN stocks     s ON s.id = il.stock_id
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
            // <-- Гарантируем, что список никогда не равен null
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
            // <-- Конвертируем в совместимый с PostgreSQL массив
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
        return new InvoiceLineEntity
        {
            Id = Guid.TryParse(l.Id, out var lid) ? lid : Guid.NewGuid(),
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