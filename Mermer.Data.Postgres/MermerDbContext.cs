using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mermer.Data.Postgres;

/// <summary>
/// Main EF Core DbContext for the Mermer Binyat ERP system on PostgreSQL.
/// Replaces the Couchbase data access layer (Mermer.Core.Couch).
/// 
/// CRITICAL: All financial columns use NUMERIC(18,4) or NUMERIC(18,8).
///           float/double is NEVER used for monetary values.
/// </summary>
public class MermerDbContext : DbContext
{
    public static string CurrentClientId { get; set; } = "default";
    public MermerDbContext(DbContextOptions<MermerDbContext> options) : base(options) { }

    // ── Reference Tables ──
    public DbSet<OfficeEntity> Offices => Set<OfficeEntity>();
    public DbSet<WarehouseEntity> Warehouses => Set<WarehouseEntity>();
    public DbSet<DepositoryEntity> Depositories => Set<DepositoryEntity>();
    public DbSet<CurrencyEntity> Currencies => Set<CurrencyEntity>();
    public DbSet<CurrencyRateEntity> CurrencyRates => Set<CurrencyRateEntity>();
    public DbSet<PartnerEntity> Partners => Set<PartnerEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    // ── Stock Management ──
    public DbSet<StockEntity> Stocks => Set<StockEntity>();
    public DbSet<StockUnitEntity> StockUnits => Set<StockUnitEntity>();
    public DbSet<StockPriceEntity> StockPrices => Set<StockPriceEntity>();
    public DbSet<StockAdditionalPriceEntity> StockAdditionalPrices => Set<StockAdditionalPriceEntity>();
    public DbSet<StockBalanceEntity> StockBalances => Set<StockBalanceEntity>();
    public DbSet<StockSlipEntity> StockSlips => Set<StockSlipEntity>();
    public DbSet<StockSlipLineEntity> StockSlipLines => Set<StockSlipLineEntity>();
    public DbSet<StockTransferEntity> StockTransfers => Set<StockTransferEntity>();
    public DbSet<StockTransferLineEntity> StockTransferLines => Set<StockTransferLineEntity>();
    public DbSet<StockRevisionEntity> StockRevisions => Set<StockRevisionEntity>();
    public DbSet<StockRevisionLineEntity> StockRevisionLines => Set<StockRevisionLineEntity>();

    public DbSet<StockOrderEntity> StockOrders => Set<StockOrderEntity>();
    public DbSet<StockOrderLineEntity> StockOrderLines => Set<StockOrderLineEntity>();
    public DbSet<StockOrderUnitConvertionEntity> StockOrderUnitConvertions => Set<StockOrderUnitConvertionEntity>();
    public DbSet<StockOrderTemplateEntity> StockOrderTemplates => Set<StockOrderTemplateEntity>();
    public DbSet<StockOrderTemplateLineEntity> StockOrderTemplateLines => Set<StockOrderTemplateLineEntity>();
    public DbSet<AggregatedStockOrderEntity> AggregatedStockOrders => Set<AggregatedStockOrderEntity>();
    public DbSet<AggregatedStockOrderLineEntity> AggregatedStockOrderLines => Set<AggregatedStockOrderLineEntity>();
    public DbSet<StockNameComposerEntity> StockNameComposers => Set<StockNameComposerEntity>();
    public DbSet<StockNameComposerValueEntity> StockNameComposerValues => Set<StockNameComposerValueEntity>();
    public DbSet<StockAlternativeEntity> StockAlternatives => Set<StockAlternativeEntity>();
    public DbSet<StockAlternativeLineEntity> StockAlternativeLines => Set<StockAlternativeLineEntity>();

    // ── Commerce ──
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<InvoiceLineEntity> InvoiceLines => Set<InvoiceLineEntity>();
    public DbSet<InvoiceDiscountEntity> InvoiceDiscounts => Set<InvoiceDiscountEntity>();
    public DbSet<InvoicePaymentEntity> InvoicePayments => Set<InvoicePaymentEntity>();
    public DbSet<InvoiceCurrencyConvertionEntity> InvoiceCurrencyConvertions => Set<InvoiceCurrencyConvertionEntity>();
    public DbSet<InvoiceStockUnitConvertionEntity> InvoiceStockUnitConvertions => Set<InvoiceStockUnitConvertionEntity>();
    public DbSet<InvoiceOverheadEntity> InvoiceOverheads => Set<InvoiceOverheadEntity>();
    public DbSet<PartnerSlipEntity> PartnerSlips { get; set; } = null!;
    public DbSet<PartnerSlipLineEntity> PartnerSlipLines { get; set; } = null!;
    public DbSet<PartnerTransferEntity> PartnerTransfers { get; set; } = null!;
    public DbSet<PartnerTransferLineEntity> PartnerTransferLines { get; set; } = null!;

    // ── Finance ──
    public DbSet<FundsSlipEntity> FundsSlips => Set<FundsSlipEntity>();
    public DbSet<FundsSlipLineEntity> FundsSlipLines => Set<FundsSlipLineEntity>();
    public DbSet<FundsTransferEntity> FundsTransfers => Set<FundsTransferEntity>();
    public DbSet<FundsTransferLineEntity> FundsTransferLines => Set<FundsTransferLineEntity>();
    public DbSet<ExpenseSlipEntity> ExpenseSlips { get; set; } = null!;
    public DbSet<ExpenseSlipLineEntity> ExpenseSlipLines { get; set; } = null!;
    public DbSet<Entities.ExpenseEntity> Expenses { get; set; } = null!;
    public DbSet<DailyFundsRegisteryEntity> DailyFundsRegisteries { get; set; } = null!;
    public DbSet<DailyFundsRegisteryLineEntity> DailyFundsRegisteryLines { get; set; } = null!;

    public DbSet<LicenseEntity> Licenses { get; set; } = null!;

    // ── Settings ──
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Offices ──
        modelBuilder.Entity<OfficeEntity>(e =>
        {
            e.ToTable("offices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Region).HasColumnName("region").HasMaxLength(200);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Warehouses ──
        modelBuilder.Entity<WarehouseEntity>(e =>
        {
            e.ToTable("warehouses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Office).WithMany(o => o.Warehouses).HasForeignKey(x => x.OfficeId);
        });

        // ── Depositories ──
        modelBuilder.Entity<DepositoryEntity>(e =>
        {
            e.ToTable("depositories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Office).WithMany(o => o.Depositories).HasForeignKey(x => x.OfficeId);
        });

        // ── Currencies ──
        modelBuilder.Entity<CurrencyEntity>(e =>
        {
            e.ToTable("currencies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Decimals).HasColumnName("decimals").HasDefaultValue(2);
            e.Property(x => x.IsDefault).HasColumnName("is_default");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Currency Rates ──
        modelBuilder.Entity<CurrencyRateEntity>(e =>
        {
            e.ToTable("currency_rates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.Property(x => x.Multiplier).HasColumnName("multiplier").HasColumnType("numeric(18,8)");
            e.Property(x => x.Divider).HasColumnName("divider").HasColumnType("numeric(18,8)");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Currency).WithMany(c => c.Rates).HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Users ──
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
            e.Property(x => x.Password).HasColumnName("password").HasMaxLength(500);
            e.Property(x => x.IsAdmin).HasColumnName("is_admin");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Partners ──
        modelBuilder.Entity<PartnerEntity>(e =>
        {
            e.ToTable("partners");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500).IsRequired();
            e.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(100);
            e.Property(x => x.Address).HasColumnName("address");
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.CreditLimit).HasColumnName("credit_limit").HasColumnType("numeric(18,4)");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Rating).HasColumnName("rating").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Stocks ──
        modelBuilder.Entity<StockEntity>(e =>
        {
            e.ToTable("stocks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500).IsRequired();
            e.Property(x => x.ShortName).HasColumnName("short_name").HasMaxLength(200);
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(100);
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Barcodes).HasColumnName("barcodes");
            e.Property(x => x.LimitMin).HasColumnName("limit_min").HasColumnType("numeric(18,4)");
            e.Property(x => x.LimitMax).HasColumnName("limit_max").HasColumnType("numeric(18,4)");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.SearchVector).HasColumnName("search_vector")
                .HasColumnType("tsvector")
                .ValueGeneratedOnAddOrUpdate();
            e.HasIndex(x => x.SearchVector).HasMethod("GIN");
        });

        // ── Stock Units ──
        modelBuilder.Entity<StockUnitEntity>(e =>
        {
            e.ToTable("stock_units");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Multiplier).HasColumnName("multiplier").HasColumnType("numeric(18,8)");
            e.Property(x => x.Divider).HasColumnName("divider").HasColumnType("numeric(18,8)");
            e.Property(x => x.IsDefault).HasColumnName("is_default");
            e.HasOne(x => x.Stock).WithMany(s => s.Units).HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Stock Prices ──
        modelBuilder.Entity<StockPriceEntity>(e =>
        {
            e.ToTable("stock_prices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.PriceGroup).HasColumnName("price_group").HasMaxLength(100);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Stock).WithMany(s => s.Prices).HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Stock Additional Prices ──
        modelBuilder.Entity<StockAdditionalPriceEntity>(e =>
        {
            e.ToTable("stock_additional_prices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.PriceGroup).HasColumnName("price_group").HasMaxLength(100);
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.HasOne(x => x.Stock).WithMany(s => s.AdditionalPrices).HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Stock Balances ──
        modelBuilder.Entity<StockBalanceEntity>(e =>
        {
            e.ToTable("stock_balances");
            e.HasKey(x => new { x.WarehouseId, x.StockId });
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.Income).HasColumnName("income").HasColumnType("numeric(18,4)");
            e.Property(x => x.Expense).HasColumnName("expense").HasColumnType("numeric(18,4)");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.Ignore(x => x.Balance);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);
            e.HasOne(x => x.Stock).WithMany().HasForeignKey(x => x.StockId);
        });

        // ── Stock Slips ──
        modelBuilder.Entity<StockSlipEntity>(e =>
        {
            e.ToTable("stock_slips");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.SlipType).HasColumnName("slip_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsStockIncome).HasColumnName("is_stock_income");
            e.Property(x => x.DisplayTotal).HasColumnName("display_total").HasColumnType("numeric(18,4)");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);
        });

        // ── Stock Slip Lines ──
        modelBuilder.Entity<StockSlipLineEntity>(e =>
        {
            e.ToTable("stock_slip_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockSlipId).HasColumnName("stock_slip_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)");
            e.Property(x => x.ActionQuantity).HasColumnName("action_quantity").HasColumnType("numeric(18,4)");
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,4)");
            e.Property(x => x.ActionTotal).HasColumnName("action_total").HasColumnType("numeric(18,4)");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.StockSlip).WithMany(s => s.Lines).HasForeignKey(x => x.StockSlipId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Stock).WithMany().HasForeignKey(x => x.StockId);
            e.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId);
        });

        // ── Stock Transfers ──
        modelBuilder.Entity<StockTransferEntity>(e =>
        {
            e.ToTable("stock_transfers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.DestinationWarehouseId).HasColumnName("destination_warehouse_id");
            e.Property(x => x.DisplayCurrencyId).HasColumnName("display_currency_id");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(100);
            e.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.ActionTotal).HasColumnName("action_total").HasColumnType("numeric(18,4)");
            e.Property(x => x.ActionReceivedTotal).HasColumnName("action_received_total").HasColumnType("numeric(18,4)");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Stock Transfer Lines ──
        modelBuilder.Entity<StockTransferLineEntity>(e =>
        {
            e.ToTable("stock_transfer_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockTransferId).HasColumnName("stock_transfer_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.ReceivedUnitId).HasColumnName("received_unit_id");
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)");
            e.Property(x => x.ReceivedQuantity).HasColumnName("received_quantity").HasColumnType("numeric(18,4)");
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,4)");
            e.Property(x => x.ActionTotal).HasColumnName("action_total").HasColumnType("numeric(18,4)");
            e.Property(x => x.ActionReceivedTotal).HasColumnName("action_received_total").HasColumnType("numeric(18,4)");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.StockTransfer).WithMany(t => t.Lines).HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Stock Revisions ──
        modelBuilder.Entity<StockRevisionEntity>(e =>
        {
            e.ToTable("stock_revisions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.FinishDate).HasColumnName("finish_date");
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.ExceedSlipId).HasColumnName("exceed_slip_id");
            e.Property(x => x.DeficitSlipId).HasColumnName("deficit_slip_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(100);
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(100);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Stock Revision Lines ──
        modelBuilder.Entity<StockRevisionLineEntity>(e =>
        {
            e.ToTable("stock_revision_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockRevisionId).HasColumnName("stock_revision_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(100);
            e.HasOne(x => x.StockRevision).WithMany(r => r.Lines).HasForeignKey(x => x.StockRevisionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Stock Orders ──
        modelBuilder.Entity<StockOrderEntity>(e =>
        {
            e.ToTable("stock_orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.PartnerId).HasColumnName("partner_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Stock Order Lines ──
        modelBuilder.Entity<StockOrderLineEntity>(e =>
        {
            e.ToTable("stock_order_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockOrderId).HasColumnName("stock_order_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.HasOne(x => x.StockOrder).WithMany(o => o.Lines).HasForeignKey(x => x.StockOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Stock Order Unit Convertions ──
        modelBuilder.Entity<StockOrderUnitConvertionEntity>(e =>
        {
            e.ToTable("stock_order_unit_convertions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockOrderId).HasColumnName("stock_order_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.Multiplier).HasColumnName("multiplier").HasColumnType("numeric(18,8)");
            e.Property(x => x.Divider).HasColumnName("divider").HasColumnType("numeric(18,8)");
            e.HasOne(x => x.StockOrder).WithMany(o => o.UnitConvertions).HasForeignKey(x => x.StockOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Stock Order Templates ──
        modelBuilder.Entity<StockOrderTemplateEntity>(e =>
        {
            e.ToTable("stock_order_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500).IsRequired();
            e.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Stock Order Template Lines ──
        modelBuilder.Entity<StockOrderTemplateLineEntity>(e =>
        {
            e.ToTable("stock_order_template_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockOrderTemplateId).HasColumnName("stock_order_template_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.HasOne(x => x.Template).WithMany(t => t.Lines).HasForeignKey(x => x.StockOrderTemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Aggregated Stock Orders ──
        modelBuilder.Entity<AggregatedStockOrderEntity>(e =>
        {
            e.ToTable("aggregated_stock_orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.PartnerId).HasColumnName("partner_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Aggregated Stock Order Lines ──
        modelBuilder.Entity<AggregatedStockOrderLineEntity>(e =>
        {
            e.ToTable("aggregated_stock_order_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AggregatedStockOrderId).HasColumnName("aggregated_stock_order_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.OrdersJson).HasColumnName("orders").HasColumnType("jsonb");
            e.HasOne(x => x.AggregatedStockOrder).WithMany(a => a.Lines).HasForeignKey(x => x.AggregatedStockOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Stock Name Composers ──
        modelBuilder.Entity<StockNameComposerEntity>(e =>
        {
            e.ToTable("stock_name_composers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Order).HasColumnName("order");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Stock Name Composer Values ──
        modelBuilder.Entity<StockNameComposerValueEntity>(e =>
        {
            e.ToTable("stock_name_composer_values");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ComposerId).HasColumnName("composer_id");
            e.Property(x => x.Order).HasColumnName("order");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500);
            e.Property(x => x.ShortName).HasColumnName("short_name").HasMaxLength(200);
            e.HasOne(x => x.Composer).WithMany(c => c.Values).HasForeignKey(x => x.ComposerId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Stock Alternatives ──
        modelBuilder.Entity<StockAlternativeEntity>(e =>
        {
            e.ToTable("stock_alternatives");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(500).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Stock Alternative Lines ──
        modelBuilder.Entity<StockAlternativeLineEntity>(e =>
        {
            e.ToTable("stock_alternative_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StockAlternativeId).HasColumnName("stock_alternative_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.HasOne(x => x.Alternative).WithMany(a => a.Lines).HasForeignKey(x => x.StockAlternativeId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Invoices ──
        modelBuilder.Entity<InvoiceEntity>(e =>
        {
            e.ToTable("invoices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.DueDate).HasColumnName("due_date");
            e.Property(x => x.InvoiceType).HasColumnName("invoice_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.DepositoryId).HasColumnName("depository_id");
            e.Property(x => x.PartnerId).HasColumnName("partner_id");
            e.Property(x => x.DisplayCurrencyId).HasColumnName("display_currency_id");
            e.Property(x => x.StockPriceGroup).HasColumnName("stock_price_group").HasMaxLength(100);
            e.Property(x => x.DebitCreditLeftAmount).HasColumnName("debit_credit_left_amount");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);
            e.HasOne(x => x.Depository).WithMany().HasForeignKey(x => x.DepositoryId);
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId);
            e.HasOne(x => x.DisplayCurrency).WithMany().HasForeignKey(x => x.DisplayCurrencyId);
        });

        // ── Invoice Lines ──
        modelBuilder.Entity<InvoiceLineEntity>(e =>
        {
            e.ToTable("invoice_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)");
            e.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.Invoice).WithMany(i => i.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Stock).WithMany().HasForeignKey(x => x.StockId);
            e.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Invoice Discounts ──
        modelBuilder.Entity<InvoiceDiscountEntity>(e =>
        {
            e.ToTable("invoice_discounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            e.Property(x => x.DiscountType).HasColumnName("discount_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.Invoice).WithMany(i => i.Discounts).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Invoice Payments ──
        modelBuilder.Entity<InvoicePaymentEntity>(e =>
        {
            e.ToTable("invoice_payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            e.Property(x => x.PaymentType).HasColumnName("payment_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.Invoice).WithMany(i => i.Payments).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Invoice Currency Convertions ──
        modelBuilder.Entity<InvoiceCurrencyConvertionEntity>(e =>
        {
            e.ToTable("invoice_currency_convertions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.Multiplier).HasColumnName("multiplier").HasColumnType("numeric(18,8)");
            e.Property(x => x.Divider).HasColumnName("divider").HasColumnType("numeric(18,8)");
            e.HasOne(x => x.Invoice).WithMany(i => i.CurrencyConvertions).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Invoice Stock Unit Convertions ──
        modelBuilder.Entity<InvoiceStockUnitConvertionEntity>(e =>
        {
            e.ToTable("invoice_stock_unit_convertions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.UnitId).HasColumnName("unit_id");
            e.Property(x => x.Multiplier).HasColumnName("multiplier").HasColumnType("numeric(18,8)");
            e.Property(x => x.Divider).HasColumnName("divider").HasColumnType("numeric(18,8)");
            e.HasOne(x => x.Invoice).WithMany(i => i.StockUnitConvertions).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Stock).WithMany().HasForeignKey(x => x.StockId);
            e.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId);
        });

        // ── Invoice Overheads ──
        modelBuilder.Entity<InvoiceOverheadEntity>(e =>
        {
            e.ToTable("invoice_overheads");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.Invoice).WithMany(i => i.Overheads).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Partner Slips ──
        modelBuilder.Entity<PartnerSlipEntity>(e =>
        {
            e.ToTable("partner_slips");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.SlipType).HasColumnName("slip_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Partner Slip Lines ──
        modelBuilder.Entity<PartnerSlipLineEntity>(e =>
        {
            e.ToTable("partner_slip_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PartnerSlipId).HasColumnName("partner_slip_id");
            e.Property(x => x.PartnerId).HasColumnName("partner_id");
            e.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.DebitCurrencyId).HasColumnName("debit_currency_id");
            e.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CreditCurrencyId).HasColumnName("credit_currency_id");
            e.HasOne(x => x.PartnerSlip).WithMany(s => s.Lines).HasForeignKey(x => x.PartnerSlipId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Partner Transfers ──
        modelBuilder.Entity<PartnerTransferEntity>(e =>
        {
            e.ToTable("partner_transfers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Partner Transfer Lines ──
        modelBuilder.Entity<PartnerTransferLineEntity>(e =>
        {
            e.ToTable("partner_transfer_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PartnerTransferId).HasColumnName("partner_transfer_id");
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.PartnerId).HasColumnName("partner_id");
            e.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.DebitCurrencyId).HasColumnName("debit_currency_id");
            e.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CreditCurrencyId).HasColumnName("credit_currency_id");
            e.HasOne(x => x.PartnerTransfer).WithMany(t => t.Lines).HasForeignKey(x => x.PartnerTransferId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Expenses ──
        modelBuilder.Entity<Entities.ExpenseEntity>(e =>
        {
            e.ToTable("expenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(100);
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Tags).HasColumnName("tags");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Expense Slips ──
        modelBuilder.Entity<ExpenseSlipEntity>(e =>
        {
            e.ToTable("expense_slips");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.DepositoryId).HasColumnName("depository_id");
            e.Property(x => x.DisplayCurrencyId).HasColumnName("display_currency_id");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags").IsRequired(false);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Expense Slip Lines ──
        modelBuilder.Entity<ExpenseSlipLineEntity>(e =>
        {
            e.ToTable("expense_slip_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ExpenseSlipId).HasColumnName("expense_slip_id");
            e.Property(x => x.ExpenseId).HasColumnName("expense_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.ExpenseSlip).WithMany(s => s.Lines).HasForeignKey(x => x.ExpenseSlipId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Daily Funds Registries ──
        modelBuilder.Entity<DailyFundsRegisteryEntity>(e =>
        {
            e.ToTable("daily_funds_registeries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.DepositoryId).HasColumnName("depository_id");
            e.Property(x => x.DisplayCurrencyId).HasColumnName("display_currency_id");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.GroupName).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags").IsRequired(false);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        });

        // ── Daily Funds Registry Lines ──
        modelBuilder.Entity<DailyFundsRegisteryLineEntity>(e =>
        {
            e.ToTable("daily_funds_registery_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RegisteryId).HasColumnName("registery_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.HasOne(x => x.Registery).WithMany(r => r.Lines).HasForeignKey(x => x.RegisteryId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Funds Slips ──
        modelBuilder.Entity<FundsSlipEntity>(e =>
        {
            e.ToTable("funds_slips");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.FundsSlipType).HasColumnName("funds_slip_type").HasMaxLength(50).IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.DepositoryId).HasColumnName("depository_id");
            e.Property(x => x.PartnerId).HasColumnName("partner_id");
            e.Property(x => x.DisplayCurrencyId).HasColumnName("display_currency_id");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags").IsRequired(false);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Office).WithMany().HasForeignKey(x => x.OfficeId);
            e.HasOne(x => x.Depository).WithMany().HasForeignKey(x => x.DepositoryId);
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId);
            e.HasOne(x => x.DisplayCurrency).WithMany().HasForeignKey(x => x.DisplayCurrencyId);
        });

        // ── Funds Slip Lines ──
        modelBuilder.Entity<FundsSlipLineEntity>(e =>
        {
            e.ToTable("funds_slip_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FundsSlipId).HasColumnName("funds_slip_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");

            e.HasOne(x => x.FundsSlip).WithMany(s => s.Lines).HasForeignKey(x => x.FundsSlipId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Funds Transfers ──
        modelBuilder.Entity<FundsTransferEntity>(e =>
        {
            e.ToTable("funds_transfers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.FromDepositoryId).HasColumnName("from_depository_id");
            e.Property(x => x.ToDepositoryId).HasColumnName("to_depository_id");
            e.Property(x => x.DisplayCurrencyId).HasColumnName("display_currency_id");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags").IsRequired(false);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.FromDepository).WithMany().HasForeignKey(x => x.FromDepositoryId);
            e.HasOne(x => x.ToDepository).WithMany().HasForeignKey(x => x.ToDepositoryId);
            e.HasOne(x => x.DisplayCurrency).WithMany().HasForeignKey(x => x.DisplayCurrencyId);
        });

        // ── Funds Transfer Lines ──
        modelBuilder.Entity<FundsTransferLineEntity>(e =>
        {
            e.ToTable("funds_transfer_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FundsTransferId).HasColumnName("funds_transfer_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.CurrencyId).HasColumnName("currency_id");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");

            e.HasOne(x => x.FundsTransfer).WithMany(t => t.Lines).HasForeignKey(x => x.FundsTransferId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId);
        });

        // ── Licenses ──
        modelBuilder.Entity<LicenseEntity>(e =>
        {
            e.ToTable("licenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Key).HasColumnName("key").IsRequired();
            e.Property(x => x.ApplicationId).HasColumnName("application_id").IsRequired();
            e.Property(x => x.ModuleId).HasColumnName("module_id").IsRequired();
            e.Property(x => x.MachineId).HasColumnName("machine_id");
            e.Property(x => x.Note).HasColumnName("note");
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.Property(x => x.ValidTill).HasColumnName("valid_till");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        });

        // ====================================================================
        // ГЛОБАЛЬНАЯ РЕГИСТРАЦИЯ ТЕНЕВЫХ СВОЙСТВ (CLIENT_ID и IS_SYNCED)
        // ====================================================================
        var syncableTypes = new HashSet<Type>
        {
            typeof(InvoiceEntity),
            typeof(StockSlipEntity),
            typeof(StockTransferEntity),
            typeof(StockRevisionEntity),
            typeof(StockOrderEntity),
            typeof(AggregatedStockOrderEntity),
            typeof(FundsSlipEntity),
            typeof(FundsTransferEntity),
            typeof(DailyFundsRegisteryEntity),
            typeof(ExpenseSlipEntity),
            typeof(PartnerSlipEntity),
            typeof(PartnerTransferEntity),
            typeof(PartnerEntity)
        };

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (clrType.Name.Contains("Migration"))
                continue;

            // 1. Теневое свойство ClientId для всех таблиц
            var clientProp = entityType.FindProperty("ClientId");
            if (clientProp == null)
            {
                modelBuilder.Entity(clrType)
                    .Property<string>("ClientId")
                    .HasColumnName("client_id")
                    .HasDefaultValue("default")
                    .IsRequired();

                modelBuilder.Entity(clrType)
                    .HasIndex("ClientId");
            }
            else
            {
                modelBuilder.Entity(clrType)
                    .Property("ClientId")
                    .HasColumnName("client_id")
                    .HasDefaultValue("default")
                    .IsRequired();
            }

            // 2. Теневое свойство IsSynced регистрируем ТОЛЬКО для основных документов
            if (syncableTypes.Contains(clrType))
            {
                var syncProp = entityType.FindProperty("IsSynced");
                if (syncProp == null)
                {
                    modelBuilder.Entity(clrType)
                        .Property<bool>("IsSynced")
                        .HasColumnName("is_synced")
                        .HasDefaultValue(false)
                        .IsRequired();

                    modelBuilder.Entity(clrType)
                        .HasIndex("IsSynced");
                }
                else
                {
                    modelBuilder.Entity(clrType)
                        .Property("IsSynced")
                        .HasColumnName("is_synced")
                        .HasDefaultValue(false)
                        .IsRequired();
                }
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var nowOffset = DateTimeOffset.UtcNow;
        var nowUtc = DateTime.UtcNow;

        var currentTenant = !string.IsNullOrWhiteSpace(CurrentClientId) && CurrentClientId != "default"
            ? CurrentClientId
            : "default";

        var addedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        foreach (var entry in addedEntries)
        {
            var clientProp = entry.Metadata.FindProperty("ClientId");
            if (clientProp != null)
            {
                var val = entry.Property("ClientId").CurrentValue as string;
                if (string.IsNullOrWhiteSpace(val) || val == "default")
                {
                    entry.Property("ClientId").CurrentValue = currentTenant;
                }
            }

            var syncProp = entry.Metadata.FindProperty("IsSynced");
            if (syncProp != null)
            {
                if (entry.Property("IsSynced").CurrentValue == null)
                {
                    entry.Property("IsSynced").CurrentValue = false;
                }
            }
        }

        var modifiedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified)
            .ToList();

        foreach (var entry in modifiedEntries)
        {
            var syncProp = entry.Metadata.FindProperty("IsSynced");
            if (syncProp != null)
            {
                bool isExplicitlyMarkedSynced = entry.Property("IsSynced").IsModified &&
                                                entry.Property("IsSynced").CurrentValue is true;

                if (!isExplicitlyMarkedSynced)
                {
                    entry.Property("IsSynced").CurrentValue = false;
                }
            }

            var updatedProp = entry.Metadata.FindProperty("UpdatedAt");
            if (updatedProp != null)
            {
                if (updatedProp.ClrType == typeof(DateTimeOffset))
                    entry.Property("UpdatedAt").CurrentValue = nowOffset;
                else if (updatedProp.ClrType == typeof(DateTime))
                    entry.Property("UpdatedAt").CurrentValue = nowUtc;
            }
        }

        var fundsLineEntries = ChangeTracker.Entries<FundsSlipLineEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        foreach (var lineEntry in fundsLineEntries)
        {
            var parentId = lineEntry.State == EntityState.Deleted
                ? lineEntry.OriginalValues.GetValue<Guid>("FundsSlipId")
                : lineEntry.Entity.FundsSlipId;

            if (parentId != Guid.Empty)
            {
                var parent = FundsSlips.Local.FirstOrDefault(p => p.Id == parentId);
                if (parent != null)
                {
                    Entry(parent).Property("IsSynced").CurrentValue = false;
                    Entry(parent).Property("UpdatedAt").CurrentValue = nowUtc;
                }
            }
        }

        var invoiceLineEntries = ChangeTracker.Entries<InvoiceLineEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        foreach (var lineEntry in invoiceLineEntries)
        {
            var parentId = lineEntry.State == EntityState.Deleted
                ? lineEntry.OriginalValues.GetValue<Guid>("InvoiceId")
                : lineEntry.Entity.InvoiceId;

            if (parentId != Guid.Empty)
            {
                var parent = Invoices.Local.FirstOrDefault(p => p.Id == parentId);
                if (parent != null)
                {
                    Entry(parent).Property("IsSynced").CurrentValue = false;
                    parent.UpdatedAt = nowOffset;
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    #region Tenant Self-Healing / Auto-Migration Helper
    public static async Task EnsureClientTenantMigratedAsync(MermerDbContext db, CancellationToken ct = default)
    {
        try
        {
            var activeKey = await db.Licenses.AsNoTracking()
                .Where(l => l.IsActive)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => l.Key)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(activeKey)) return;

            string clientFromToken = ExtractClientNameFromToken(activeKey);
            if (string.IsNullOrWhiteSpace(clientFromToken) || clientFromToken.Equals("default", StringComparison.OrdinalIgnoreCase))
                return;

            CurrentClientId = clientFromToken;

            bool hasLegacyDefault = await db.Invoices.AnyAsync(i => EF.Property<string>(i, "ClientId") == "default", ct)
                                 || await db.Stocks.AnyAsync(s => EF.Property<string>(s, "ClientId") == "default", ct);

            if (hasLegacyDefault)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[TENANT MIGRATION] Обнаружены данные со значением 'default'. Привязываем к клиенту '{clientFromToken}'...");
                Console.ResetColor();

                var tables = new[]
                {
                    "offices", "warehouses", "depositories", "currencies", "currency_rates",
                    "partners", "partner_slips", "partner_slip_lines", "partner_transfers", "partner_transfer_lines",
                    "funds_slips", "funds_slip_lines", "funds_transfers", "funds_transfer_lines",
                    "daily_funds_registeries", "daily_funds_registery_lines", "expenses", "expense_slips", "expense_slip_lines",
                    "stocks", "stock_units", "stock_prices", "stock_additional_prices", "stock_balances",
                    "stock_alternatives", "stock_alternative_lines", "stock_name_composers", "stock_name_composer_values",
                    "stock_slips", "stock_slip_lines", "stock_transfers", "stock_transfer_lines",
                    "stock_revisions", "stock_revision_lines", "stock_orders", "stock_order_lines",
                    "stock_order_unit_convertions", "stock_order_templates", "stock_order_template_lines",
                    "aggregated_stock_orders", "aggregated_stock_order_lines", "invoices", "invoice_lines",
                    "invoice_discounts", "invoice_payments", "invoice_overheads", "invoice_currency_convertions",
                    "invoice_stock_unit_convertions", "users", "roles"
                };

                foreach (var tbl in tables)
                {
                    try
                    {
                        await db.Database.ExecuteSqlRawAsync(
                            $"UPDATE \"{tbl}\" SET client_id = {{0}} WHERE client_id = 'default' OR client_id IS NULL;",
                            new object[] { clientFromToken }, ct);
                    }
                    catch { }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[TENANT MIGRATION] Все таблицы успешно привязаны к client_id = '{clientFromToken}'.");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TENANT AUTO-HEAL ERROR]: {ex.Message}");
        }
    }

    public static string ExtractClientNameFromToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "default";

        try
        {
            var parts = token.Trim().Split('.');
            if (parts.Length >= 2)
            {
                string payloadBase64 = parts[0].Replace('-', '+').Replace('_', '/');
                switch (payloadBase64.Length % 4)
                {
                    case 2: payloadBase64 += "=="; break;
                    case 3: payloadBase64 += "="; break;
                }

                var jsonBytes = Convert.FromBase64String(payloadBase64);
                using var doc = JsonDocument.Parse(jsonBytes);
                if (doc.RootElement.TryGetProperty("c", out var cProp))
                {
                    var val = cProp.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
        }
        catch { }

        return "default";
    }
    #endregion
}