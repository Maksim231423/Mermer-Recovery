using Microsoft.EntityFrameworkCore;
using Mermer.Data.Postgres.Entities;

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
    public DbSet<ExpenseSlipEntity> ExpenseSlips { get; set; }
    public DbSet<ExpenseSlipLineEntity> ExpenseSlipLines { get; set; }

    public DbSet<Entities.ExpenseEntity> Expenses { get; set; }

    public DbSet<DailyFundsRegisteryEntity> DailyFundsRegisteries { get; set; }
    public DbSet<DailyFundsRegisteryLineEntity> DailyFundsRegisteryLines { get; set; }


    public DbSet<LicenseEntity> Licenses { get; set; }

    // settings
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

        // ── Stock Balances ── (composite PK)
        modelBuilder.Entity<StockBalanceEntity>(e =>
        {
            e.ToTable("stock_balances");
            e.HasKey(x => new { x.WarehouseId, x.StockId });
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.StockId).HasColumnName("stock_id");
            e.Property(x => x.Income).HasColumnName("income").HasColumnType("numeric(18,4)");
            e.Property(x => x.Expense).HasColumnName("expense").HasColumnType("numeric(18,4)");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.Ignore(x => x.Balance); // Computed in C#
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);
            e.HasOne(x => x.Stock).WithMany().HasForeignKey(x => x.StockId);
        });

        // ── Stock Slips (Складские ордера) ──
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

            // ── Добавленные связи для ордера ──
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId);
        });

        // ── Stock Slip Lines (Строки складских ордеров) ──
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

            // Связь один-ко-многим: Ордер -> Строки
            e.HasOne(x => x.StockSlip)
             .WithMany(s => s.Lines)
             .HasForeignKey(x => x.StockSlipId)
             .OnDelete(DeleteBehavior.Cascade);

            // ── Добавленные связи для товаров и единиц измерения ──
            e.HasOne(x => x.Stock).WithMany().HasForeignKey(x => x.StockId);
            e.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId);
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

        // ── Funds Slips ──
        modelBuilder.Entity<FundsSlipEntity>(e =>
        {
            e.ToTable("funds_slips");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.FundsSlipType).HasColumnName("funds_slip_type").HasMaxLength(20).IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(200);
            e.Property(x => x.OfficeId).HasColumnName("office_id");
            e.Property(x => x.DepositoryId).HasColumnName("depository_id");
            e.Property(x => x.PartnerId).HasColumnName("partner_id");
            e.Property(x => x.DisplayCurrencyId).HasColumnName("display_currency_id");
            e.Property(x => x.IsCompleted).HasColumnName("is_completed");
            e.Property(x => x.IsDisabled).HasColumnName("is_disabled");
            e.Property(x => x.Group).HasColumnName("group_name").HasMaxLength(200);
            e.Property(x => x.Tags).HasColumnName("tags");
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
            e.Property(x => x.Tags).HasColumnName("tags");
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
    }
}
