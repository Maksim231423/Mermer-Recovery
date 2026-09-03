-- =============================================================================
-- Mermer ERP — Complete Local SQLite Cache Schema
-- Version: 1.8.1 | 100% Parity with PostgreSQL Schema + Sync Engine
-- =============================================================================

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. BASE ENTERPRISE & SECURITY
-- ─────────────────────────────────────────────────────────────────────────────

-- 1. Offices
CREATE TABLE IF NOT EXISTS offices (
    id           TEXT PRIMARY KEY,
    name         TEXT NOT NULL,
    region       TEXT,
    description  TEXT,
    tags         TEXT,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1,
    sync_state   TEXT    NOT NULL DEFAULT 'synced',
    last_synced  TEXT
);

-- 2. Warehouses
CREATE TABLE IF NOT EXISTS warehouses (
    id           TEXT PRIMARY KEY,
    office_id    TEXT REFERENCES offices(id),
    name         TEXT NOT NULL,
    description  TEXT,
    tags         TEXT,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1,
    sync_state   TEXT    NOT NULL DEFAULT 'synced',
    last_synced  TEXT
);
CREATE INDEX IF NOT EXISTS ix_warehouses_office ON warehouses(office_id);

-- 3. Depositories
CREATE TABLE IF NOT EXISTS depositories (
    id           TEXT PRIMARY KEY,
    office_id    TEXT REFERENCES offices(id),
    name         TEXT NOT NULL,
    description  TEXT,
    tags         TEXT,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1,
    sync_state   TEXT    NOT NULL DEFAULT 'synced',
    last_synced  TEXT
);
CREATE INDEX IF NOT EXISTS ix_depositories_office ON depositories(office_id);

-- 4. Currencies
CREATE TABLE IF NOT EXISTS currencies (
    id           TEXT PRIMARY KEY,
    name         TEXT NOT NULL,
    decimals     INTEGER NOT NULL DEFAULT 2,
    is_default   INTEGER NOT NULL DEFAULT 0,
    description  TEXT,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1,
    sync_state   TEXT    NOT NULL DEFAULT 'synced',
    last_synced  TEXT
);

-- 5. Currency Rates
CREATE TABLE IF NOT EXISTS currency_rates (
    id           TEXT PRIMARY KEY,
    currency_id  TEXT NOT NULL REFERENCES currencies(id) ON DELETE CASCADE,
    valid_from   TEXT NOT NULL,
    multiplier   NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider      NUMERIC(18,8) NOT NULL DEFAULT 1,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_currency_rates_lookup ON currency_rates(currency_id, valid_from DESC);

-- 6. Users
CREATE TABLE IF NOT EXISTS users (
    id           TEXT PRIMARY KEY,
    username     TEXT NOT NULL UNIQUE,
    password     TEXT NOT NULL DEFAULT '',
    is_admin     INTEGER NOT NULL DEFAULT 0,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    description  TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- 7. Roles
CREATE TABLE IF NOT EXISTS roles (
    id             TEXT PRIMARY KEY,
    name           TEXT NOT NULL,
    description    TEXT,
    authorizations TEXT,
    is_disabled    INTEGER NOT NULL DEFAULT 0,
    created_at     TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at     TEXT NOT NULL DEFAULT (datetime('now')),
    row_version    INTEGER NOT NULL DEFAULT 1
);

-- 8. User Roles
CREATE TABLE IF NOT EXISTS user_roles (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_id TEXT NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. CRM & PARTNERS
-- ─────────────────────────────────────────────────────────────────────────────

-- 9. Partners
CREATE TABLE IF NOT EXISTS partners (
    id           TEXT PRIMARY KEY,
    code         TEXT,
    name         TEXT NOT NULL,
    phone        TEXT,
    address      TEXT,
    group_name   TEXT,
    credit_limit NUMERIC(18,4),
    tags         TEXT,
    description  TEXT,
    rating       NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id  TEXT REFERENCES currencies(id),
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1,
    sync_state   TEXT    NOT NULL DEFAULT 'synced',
    last_synced  TEXT
);
CREATE INDEX IF NOT EXISTS ix_partners_code ON partners(code);
CREATE INDEX IF NOT EXISTS ix_partners_name ON partners(name);

-- 10. Partner Slips
CREATE TABLE IF NOT EXISTS partner_slips (
    id           TEXT PRIMARY KEY,
    code         TEXT NOT NULL,
    date         TEXT NOT NULL DEFAULT (datetime('now')),
    slip_type    TEXT NOT NULL,
    office_id    TEXT REFERENCES offices(id),
    user_id      TEXT REFERENCES users(id),
    user_name    TEXT,
    is_completed INTEGER NOT NULL DEFAULT 1,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    group_name   TEXT,
    tags         TEXT,
    description  TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_partner_slips_date ON partner_slips(date DESC);

-- 11. Partner Slip Lines
CREATE TABLE IF NOT EXISTS partner_slip_lines (
    id                 TEXT PRIMARY KEY,
    partner_slip_id    TEXT NOT NULL REFERENCES partner_slips(id) ON DELETE CASCADE,
    partner_id         TEXT REFERENCES partners(id),
    debit_amount       NUMERIC(18,4) NOT NULL DEFAULT 0,
    debit_currency_id  TEXT REFERENCES currencies(id),
    credit_amount      NUMERIC(18,4) NOT NULL DEFAULT 0,
    credit_currency_id TEXT REFERENCES currencies(id)
);
CREATE INDEX IF NOT EXISTS ix_partner_slip_lines_slip ON partner_slip_lines(partner_slip_id);

-- 12. Partner Transfers
CREATE TABLE IF NOT EXISTS partner_transfers (
    id           TEXT PRIMARY KEY,
    code         TEXT NOT NULL,
    date         TEXT NOT NULL DEFAULT (datetime('now')),
    user_id      TEXT REFERENCES users(id),
    user_name    TEXT,
    is_completed INTEGER NOT NULL DEFAULT 1,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    group_name   TEXT,
    tags         TEXT,
    description  TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- 13. Partner Transfer Lines
CREATE TABLE IF NOT EXISTS partner_transfer_lines (
    id                  TEXT PRIMARY KEY,
    partner_transfer_id TEXT NOT NULL REFERENCES partner_transfers(id) ON DELETE CASCADE,
    office_id           TEXT REFERENCES offices(id),
    partner_id          TEXT REFERENCES partners(id),
    debit_amount        NUMERIC(18,4) NOT NULL DEFAULT 0,
    debit_currency_id   TEXT REFERENCES currencies(id),
    credit_amount       NUMERIC(18,4) NOT NULL DEFAULT 0,
    credit_currency_id  TEXT REFERENCES currencies(id)
);

-- 14. Partner Actions
CREATE TABLE IF NOT EXISTS partner_actions (
    id          TEXT PRIMARY KEY,
    partner_id  TEXT NOT NULL REFERENCES partners(id) ON DELETE CASCADE,
    office_id   TEXT REFERENCES offices(id),
    action_type TEXT NOT NULL CHECK (action_type IN ('Debit','Credit')),
    amount      NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id TEXT REFERENCES currencies(id),
    description TEXT,
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS ix_partner_actions_partner ON partner_actions(partner_id);

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. STOCKS, NOMENCLATURE & PRICES
-- ─────────────────────────────────────────────────────────────────────────────

-- 15. Stocks
CREATE TABLE IF NOT EXISTS stocks (
    id           TEXT PRIMARY KEY,
    code         TEXT,
    name         TEXT NOT NULL,
    short_name   TEXT,
    type         TEXT,
    group_name   TEXT,
    tags         TEXT,
    barcodes     TEXT,
    limit_min    NUMERIC(18,4),
    limit_max    NUMERIC(18,4),
    description  TEXT,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1,
    sync_state   TEXT    NOT NULL DEFAULT 'synced',
    last_synced  TEXT
);
CREATE INDEX IF NOT EXISTS ix_stocks_code ON stocks(code);
CREATE INDEX IF NOT EXISTS ix_stocks_disabled ON stocks(is_disabled);

-- FTS5 Search Engine
CREATE VIRTUAL TABLE IF NOT EXISTS stocks_fts USING fts5(
    stock_id UNINDEXED,
    code,
    name,
    short_name,
    barcodes,
    tokenize = 'unicode61 remove_diacritics 2'
);

CREATE TRIGGER IF NOT EXISTS stocks_ai AFTER INSERT ON stocks BEGIN
    INSERT INTO stocks_fts(stock_id, code, name, short_name, barcodes)
    VALUES (NEW.id, COALESCE(NEW.code,''), NEW.name, COALESCE(NEW.short_name,''), COALESCE(NEW.barcodes,''));
END;
CREATE TRIGGER IF NOT EXISTS stocks_au AFTER UPDATE ON stocks BEGIN
    UPDATE stocks_fts
       SET code = COALESCE(NEW.code,''),
           name = NEW.name,
           short_name = COALESCE(NEW.short_name,''),
           barcodes   = COALESCE(NEW.barcodes,'')
     WHERE stock_id = NEW.id;
END;
CREATE TRIGGER IF NOT EXISTS stocks_ad AFTER DELETE ON stocks BEGIN
    DELETE FROM stocks_fts WHERE stock_id = OLD.id;
END;

-- 16. Stock Units
CREATE TABLE IF NOT EXISTS stock_units (
    id           TEXT PRIMARY KEY,
    stock_id     TEXT NOT NULL REFERENCES stocks(id) ON DELETE CASCADE,
    name         TEXT NOT NULL,
    multiplier   NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider      NUMERIC(18,8) NOT NULL DEFAULT 1,
    is_default   INTEGER NOT NULL DEFAULT 0,
    row_version  INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_stock_units_stock ON stock_units(stock_id);

-- 17. Stock Prices
CREATE TABLE IF NOT EXISTS stock_prices (
    id           TEXT PRIMARY KEY,
    stock_id     TEXT NOT NULL REFERENCES stocks(id) ON DELETE CASCADE,
    valid_from   TEXT NOT NULL,
    price        NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id  TEXT REFERENCES currencies(id),
    price_group  TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_stock_prices_lookup ON stock_prices(stock_id, price_group, valid_from DESC);

-- 18. Stock Additional Prices
CREATE TABLE IF NOT EXISTS stock_additional_prices (
    id           TEXT PRIMARY KEY,
    stock_id     TEXT NOT NULL REFERENCES stocks(id) ON DELETE CASCADE,
    price        NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id  TEXT REFERENCES currencies(id),
    price_group  TEXT,
    valid_from   TEXT NOT NULL,
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- 19. Stock Name Composers
CREATE TABLE IF NOT EXISTS stock_name_composers (
    id           TEXT PRIMARY KEY,
    "order"      INTEGER NOT NULL DEFAULT 0,
    name         TEXT NOT NULL,
    description  TEXT,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 20. Stock Name Composer Values
CREATE TABLE IF NOT EXISTS stock_name_composer_values (
    id           TEXT PRIMARY KEY,
    composer_id  TEXT NOT NULL REFERENCES stock_name_composers(id) ON DELETE CASCADE,
    "order"      INTEGER NOT NULL DEFAULT 0,
    name         TEXT,
    short_name   TEXT
);

-- 21. Stock Alternatives
CREATE TABLE IF NOT EXISTS stock_alternatives (
    id           TEXT PRIMARY KEY,
    name         TEXT NOT NULL,
    description  TEXT,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 22. Stock Alternative Lines
CREATE TABLE IF NOT EXISTS stock_alternative_lines (
    id                   TEXT PRIMARY KEY,
    stock_alternative_id TEXT NOT NULL REFERENCES stock_alternatives(id) ON DELETE CASCADE,
    stock_id             TEXT REFERENCES stocks(id)
);

-- 23. Stock Balances
CREATE TABLE IF NOT EXISTS stock_balances (
    warehouse_id TEXT NOT NULL REFERENCES warehouses(id),
    stock_id     TEXT NOT NULL REFERENCES stocks(id),
    income       NUMERIC(18,4) NOT NULL DEFAULT 0,
    expense      NUMERIC(18,4) NOT NULL DEFAULT 0,
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (warehouse_id, stock_id)
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. WAREHOUSING TRANSACTIONS
-- ─────────────────────────────────────────────────────────────────────────────

-- 24. Stock Slips
CREATE TABLE IF NOT EXISTS stock_slips (
    id              TEXT PRIMARY KEY,
    code            TEXT,
    slip_type       TEXT NOT NULL,
    is_completed    INTEGER NOT NULL DEFAULT 0,
    is_stock_income INTEGER NOT NULL DEFAULT 0,
    display_total   NUMERIC(18,4) NOT NULL DEFAULT 0,
    description     TEXT,
    group_name      TEXT,
    tags            TEXT,
    date            TEXT NOT NULL DEFAULT (datetime('now')),
    user_id         TEXT REFERENCES users(id),
    warehouse_id    TEXT REFERENCES warehouses(id),
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now')),
    row_version     INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_stock_slips_date ON stock_slips(date DESC);

-- 25. Stock Slip Lines
CREATE TABLE IF NOT EXISTS stock_slip_lines (
    id              TEXT PRIMARY KEY,
    stock_slip_id   TEXT NOT NULL REFERENCES stock_slips(id) ON DELETE CASCADE,
    stock_id        TEXT REFERENCES stocks(id),
    unit_id         TEXT REFERENCES stock_units(id),
    quantity        NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_quantity NUMERIC(18,4) NOT NULL DEFAULT 0,
    price           NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_total    NUMERIC(18,4) NOT NULL DEFAULT 0,
    sort_order      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_stock_slip_lines_slip ON stock_slip_lines(stock_slip_id);

-- 26. Stock Transfers
CREATE TABLE IF NOT EXISTS stock_transfers (
    id                       TEXT PRIMARY KEY,
    code                     TEXT,
    date                     TEXT NOT NULL DEFAULT (datetime('now')),
    warehouse_id             TEXT REFERENCES warehouses(id),
    destination_warehouse_id TEXT REFERENCES warehouses(id),
    display_currency_id      TEXT REFERENCES currencies(id),
    is_completed             INTEGER NOT NULL DEFAULT 0,
    is_disabled              INTEGER NOT NULL DEFAULT 0,
    user_name                TEXT,
    group_name               TEXT,
    description              TEXT,
    tags                     TEXT,
    action_total             NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_received_total    NUMERIC(18,4) NOT NULL DEFAULT 0,
    created_at               TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at               TEXT NOT NULL DEFAULT (datetime('now')),
    row_version              INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_stock_transfers_date ON stock_transfers(date DESC);

-- 27. Stock Transfer Lines
CREATE TABLE IF NOT EXISTS stock_transfer_lines (
    id                    TEXT PRIMARY KEY,
    stock_transfer_id     TEXT NOT NULL REFERENCES stock_transfers(id) ON DELETE CASCADE,
    stock_id              TEXT REFERENCES stocks(id),
    unit_id               TEXT REFERENCES stock_units(id),
    received_unit_id      TEXT REFERENCES stock_units(id),
    quantity              NUMERIC(18,4) NOT NULL DEFAULT 0,
    received_quantity     NUMERIC(18,4) NOT NULL DEFAULT 0,
    price                 NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_total          NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_received_total NUMERIC(18,4) NOT NULL DEFAULT 0,
    sort_order            INTEGER NOT NULL DEFAULT 0
);

-- 28. Stock Revisions
CREATE TABLE IF NOT EXISTS stock_revisions (
    id              TEXT PRIMARY KEY,
    code            TEXT,
    date            TEXT NOT NULL DEFAULT (datetime('now')),
    finish_date     TEXT,
    warehouse_id    TEXT REFERENCES warehouses(id),
    exceed_slip_id  TEXT,
    deficit_slip_id TEXT,
    user_id         TEXT REFERENCES users(id),
    user_name       TEXT,
    is_completed    INTEGER NOT NULL DEFAULT 0,
    is_disabled     INTEGER NOT NULL DEFAULT 0,
    group_name      TEXT,
    tags            TEXT,
    description     TEXT,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now')),
    row_version     INTEGER NOT NULL DEFAULT 1
);

-- 29. Stock Revision Lines
CREATE TABLE IF NOT EXISTS stock_revision_lines (
    id                TEXT PRIMARY KEY,
    stock_revision_id TEXT NOT NULL REFERENCES stock_revisions(id) ON DELETE CASCADE,
    stock_id          TEXT REFERENCES stocks(id),
    date              TEXT NOT NULL DEFAULT (datetime('now')),
    quantity          NUMERIC(18,4) NOT NULL DEFAULT 0,
    unit_id           TEXT REFERENCES stock_units(id),
    price             NUMERIC(18,4),
    currency_id       TEXT REFERENCES currencies(id),
    user_id           TEXT REFERENCES users(id),
    user_name         TEXT,
    created_at        TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at        TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 30. Stock Orders
CREATE TABLE IF NOT EXISTS stock_orders (
    id           TEXT PRIMARY KEY,
    code         TEXT,
    date         TEXT NOT NULL DEFAULT (datetime('now')),
    warehouse_id TEXT REFERENCES warehouses(id),
    partner_id   TEXT REFERENCES partners(id),
    user_id      TEXT REFERENCES users(id),
    user_name    TEXT,
    is_completed INTEGER NOT NULL DEFAULT 0,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    group_name   TEXT,
    tags         TEXT,
    description  TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- 31. Stock Order Lines
CREATE TABLE IF NOT EXISTS stock_order_lines (
    id             TEXT PRIMARY KEY,
    stock_order_id TEXT NOT NULL REFERENCES stock_orders(id) ON DELETE CASCADE,
    stock_id       TEXT REFERENCES stocks(id),
    quantity       NUMERIC(18,4) NOT NULL DEFAULT 0,
    unit_id        TEXT REFERENCES stock_units(id)
);

-- 32. Stock Order Unit Convertions
CREATE TABLE IF NOT EXISTS stock_order_unit_convertions (
    id             TEXT PRIMARY KEY,
    stock_order_id TEXT NOT NULL REFERENCES stock_orders(id) ON DELETE CASCADE,
    stock_id       TEXT REFERENCES stocks(id),
    unit_id        TEXT REFERENCES stock_units(id),
    multiplier     NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider        NUMERIC(18,8) NOT NULL DEFAULT 1
);

-- 33. Stock Order Templates
CREATE TABLE IF NOT EXISTS stock_order_templates (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    group_name  TEXT,
    tags        TEXT,
    description TEXT,
    is_disabled INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 34. Stock Order Template Lines
CREATE TABLE IF NOT EXISTS stock_order_template_lines (
    id                      TEXT PRIMARY KEY,
    stock_order_template_id TEXT NOT NULL REFERENCES stock_order_templates(id) ON DELETE CASCADE,
    stock_id                TEXT REFERENCES stocks(id)
);

-- 35. Aggregated Stock Orders
CREATE TABLE IF NOT EXISTS aggregated_stock_orders (
    id           TEXT PRIMARY KEY,
    code         TEXT,
    date         TEXT NOT NULL DEFAULT (datetime('now')),
    warehouse_id TEXT REFERENCES warehouses(id),
    partner_id   TEXT REFERENCES partners(id),
    user_id      TEXT REFERENCES users(id),
    user_name    TEXT,
    is_completed INTEGER NOT NULL DEFAULT 0,
    is_disabled  INTEGER NOT NULL DEFAULT 0,
    group_name   TEXT,
    tags         TEXT,
    description  TEXT,
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at   TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 36. Aggregated Stock Order Lines
CREATE TABLE IF NOT EXISTS aggregated_stock_order_lines (
    id                        TEXT PRIMARY KEY,
    aggregated_stock_order_id TEXT NOT NULL REFERENCES aggregated_stock_orders(id) ON DELETE CASCADE,
    stock_id                  TEXT REFERENCES stocks(id),
    unit_id                   TEXT REFERENCES stock_units(id),
    orders                    TEXT NOT NULL DEFAULT '{}'
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. COMMERCE — INVOICES & BILLS
-- ─────────────────────────────────────────────────────────────────────────────

-- 37. Invoices
CREATE TABLE IF NOT EXISTS invoices (
    id                       TEXT PRIMARY KEY,
    code                     TEXT,
    date                     TEXT NOT NULL,
    due_date                 TEXT,
    invoice_type             TEXT NOT NULL CHECK (invoice_type IN ('Purchase','PurchaseReturn','Sales','SalesReturn')),
    user_id                  TEXT,
    user_name                TEXT,
    office_id                TEXT REFERENCES offices(id),
    warehouse_id             TEXT REFERENCES warehouses(id),
    depository_id            TEXT REFERENCES depositories(id),
    partner_id               TEXT REFERENCES partners(id),
    display_currency_id      TEXT REFERENCES currencies(id),
    stock_price_group        TEXT,
    debit_credit_left_amount INTEGER NOT NULL DEFAULT 0,
    is_completed             INTEGER NOT NULL DEFAULT 0,
    is_disabled              INTEGER NOT NULL DEFAULT 0,
    group_name               TEXT,
    tags                     TEXT,
    description              TEXT,
    created_at               TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at               TEXT NOT NULL DEFAULT (datetime('now')),
    row_version              INTEGER NOT NULL DEFAULT 1,
    sync_state               TEXT    NOT NULL DEFAULT 'synced',
    last_synced              TEXT
);
CREATE INDEX IF NOT EXISTS ix_invoices_date ON invoices(date DESC);

-- 38. Invoice Lines
CREATE TABLE IF NOT EXISTS invoice_lines (
    id           TEXT PRIMARY KEY,
    invoice_id   TEXT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    source_id    TEXT,
    stock_id     TEXT REFERENCES stocks(id),
    unit_id      TEXT REFERENCES stock_units(id),
    quantity     NUMERIC(18,4) NOT NULL DEFAULT 0,
    price        NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id  TEXT REFERENCES currencies(id),
    sort_order   INTEGER NOT NULL DEFAULT 0,
    row_version  INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_invoice_lines_invoice ON invoice_lines(invoice_id);
CREATE INDEX IF NOT EXISTS ix_invoice_lines_stock ON invoice_lines(stock_id);

-- 39. Invoice Currency Convertions
CREATE TABLE IF NOT EXISTS invoice_currency_convertions (
    id           TEXT PRIMARY KEY,
    invoice_id   TEXT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    currency_id  TEXT NOT NULL REFERENCES currencies(id),
    multiplier   NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider      NUMERIC(18,8) NOT NULL DEFAULT 1,
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- 40. Invoice Stock Unit Convertions
CREATE TABLE IF NOT EXISTS invoice_stock_unit_convertions (
    id           TEXT PRIMARY KEY,
    invoice_id   TEXT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    stock_id     TEXT NOT NULL REFERENCES stocks(id),
    unit_id      TEXT NOT NULL REFERENCES stock_units(id),
    multiplier   NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider      NUMERIC(18,8) NOT NULL DEFAULT 1,
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- 41. Invoice Discounts
CREATE TABLE IF NOT EXISTS invoice_discounts (
    id            TEXT PRIMARY KEY,
    invoice_id    TEXT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    discount_type TEXT NOT NULL CHECK (discount_type IN ('Flat','Percentage')),
    amount        NUMERIC(18,4) NOT NULL DEFAULT 0,
    description   TEXT,
    sort_order    INTEGER NOT NULL DEFAULT 0,
    row_version   INTEGER NOT NULL DEFAULT 1
);

-- 42. Invoice Payments
CREATE TABLE IF NOT EXISTS invoice_payments (
    id           TEXT PRIMARY KEY,
    invoice_id   TEXT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    payment_type TEXT NOT NULL CHECK (payment_type IN ('Payment','Change')),
    amount       NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id  TEXT REFERENCES currencies(id),
    sort_order   INTEGER NOT NULL DEFAULT 0,
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- 43. Invoice Overheads
CREATE TABLE IF NOT EXISTS invoice_overheads (
    id           TEXT PRIMARY KEY,
    invoice_id   TEXT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    amount       NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id  TEXT REFERENCES currencies(id),
    description  TEXT,
    sort_order   INTEGER NOT NULL DEFAULT 0,
    row_version  INTEGER NOT NULL DEFAULT 1
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 6. FUNDS MANAGEMENT & EXPENSES
-- ─────────────────────────────────────────────────────────────────────────────

-- 44. Expenses
CREATE TABLE IF NOT EXISTS expenses (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    type        TEXT,
    group_name  TEXT,
    description TEXT,
    tags        TEXT,
    is_disabled INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now')),
    row_version INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_expenses_name ON expenses(name);

-- 45. Funds Slips
CREATE TABLE IF NOT EXISTS funds_slips (
    id                  TEXT PRIMARY KEY,
    code                TEXT,
    date                TEXT NOT NULL DEFAULT (datetime('now')),
    funds_slip_type     TEXT NOT NULL,
    user_id             TEXT REFERENCES users(id),
    user_name           TEXT,
    office_id           TEXT REFERENCES offices(id),
    depository_id       TEXT REFERENCES depositories(id),
    partner_id          TEXT REFERENCES partners(id),
    display_currency_id TEXT REFERENCES currencies(id),
    is_completed        INTEGER NOT NULL DEFAULT 0,
    is_disabled         INTEGER NOT NULL DEFAULT 0,
    group_name          TEXT,
    tags                TEXT,
    description         TEXT,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now')),
    row_version         INTEGER NOT NULL DEFAULT 1
);

-- 46. Funds Slip Lines
CREATE TABLE IF NOT EXISTS funds_slip_lines (
    id            TEXT PRIMARY KEY,
    funds_slip_id TEXT NOT NULL REFERENCES funds_slips(id) ON DELETE CASCADE,
    amount        NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id   TEXT REFERENCES currencies(id),
    sort_order    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_funds_slip_lines_slip ON funds_slip_lines(funds_slip_id);

-- 47. Funds Transfers
CREATE TABLE IF NOT EXISTS funds_transfers (
    id                  TEXT PRIMARY KEY,
    code                TEXT,
    date                TEXT NOT NULL DEFAULT (datetime('now')),
    user_id             TEXT REFERENCES users(id),
    user_name           TEXT,
    from_depository_id  TEXT REFERENCES depositories(id),
    to_depository_id    TEXT REFERENCES depositories(id),
    display_currency_id TEXT REFERENCES currencies(id),
    is_completed        INTEGER NOT NULL DEFAULT 0,
    is_disabled         INTEGER NOT NULL DEFAULT 0,
    group_name          TEXT,
    tags                TEXT,
    description         TEXT,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now')),
    row_version         INTEGER NOT NULL DEFAULT 1
);

-- 48. Funds Transfer Lines
CREATE TABLE IF NOT EXISTS funds_transfer_lines (
    id                TEXT PRIMARY KEY,
    funds_transfer_id TEXT NOT NULL REFERENCES funds_transfers(id) ON DELETE CASCADE,
    amount            NUMERIC(18,4) NOT NULL DEFAULT 0,
    received_amount   NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id       TEXT REFERENCES currencies(id),
    sort_order        INTEGER NOT NULL DEFAULT 0
);

-- 49. Expense Slips
CREATE TABLE IF NOT EXISTS expense_slips (
    id                  TEXT PRIMARY KEY,
    code                TEXT,
    date                TEXT NOT NULL DEFAULT (datetime('now')),
    user_id             TEXT REFERENCES users(id),
    user_name           TEXT,
    office_id           TEXT REFERENCES offices(id),
    depository_id       TEXT REFERENCES depositories(id),
    display_currency_id TEXT REFERENCES currencies(id),
    is_completed        INTEGER NOT NULL DEFAULT 0,
    is_disabled         INTEGER NOT NULL DEFAULT 0,
    group_name          TEXT,
    tags                TEXT,
    description         TEXT,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now')),
    row_version         INTEGER NOT NULL DEFAULT 1
);

-- 50. Expense Slip Lines
CREATE TABLE IF NOT EXISTS expense_slip_lines (
    id              TEXT PRIMARY KEY,
    expense_slip_id TEXT NOT NULL REFERENCES expense_slips(id) ON DELETE CASCADE,
    expense_id      TEXT REFERENCES expenses(id),
    amount          NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     TEXT REFERENCES currencies(id),
    sort_order      INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_expense_slip_lines_slip ON expense_slip_lines(expense_slip_id);

-- 51. Daily Funds Registeries
CREATE TABLE IF NOT EXISTS daily_funds_registeries (
    id                  TEXT PRIMARY KEY,
    code                TEXT,
    date                TEXT NOT NULL DEFAULT (datetime('now')),
    user_id             TEXT REFERENCES users(id),
    user_name           TEXT,
    depository_id       TEXT REFERENCES depositories(id),
    display_currency_id TEXT REFERENCES currencies(id),
    is_completed        INTEGER NOT NULL DEFAULT 0,
    is_disabled         INTEGER NOT NULL DEFAULT 0,
    group_name          TEXT,
    tags                TEXT,
    description         TEXT,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now')),
    row_version         INTEGER NOT NULL DEFAULT 1
);

-- 52. Daily Funds Registery Lines
CREATE TABLE IF NOT EXISTS daily_funds_registery_lines (
    id           TEXT PRIMARY KEY,
    registery_id TEXT NOT NULL REFERENCES daily_funds_registeries(id) ON DELETE CASCADE,
    amount       NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id  TEXT REFERENCES currencies(id),
    sort_order   INTEGER NOT NULL DEFAULT 0
);

-- ─────────────────────────────────────────────────────────────────────────────
-- 7. SYNC ENGINE & QUEUE
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS sync_state (
    table_name     TEXT PRIMARY KEY,
    last_pulled_at TEXT,
    last_pushed_at TEXT,
    last_error     TEXT,
    last_run_at    TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS sync_outbox (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    table_name TEXT NOT NULL,
    row_id     TEXT NOT NULL,
    operation  TEXT NOT NULL CHECK (operation IN ('insert','update','delete')),
    payload    TEXT,
    queued_at  TEXT NOT NULL DEFAULT (datetime('now')),
    attempt    INTEGER NOT NULL DEFAULT 0,
    last_error TEXT
);
CREATE INDEX IF NOT EXISTS ix_sync_outbox_queued ON sync_outbox(queued_at, table_name);

CREATE TABLE IF NOT EXISTS sync_conflicts (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    table_name  TEXT NOT NULL,
    row_id      TEXT NOT NULL,
    local_data  TEXT NOT NULL,
    server_data TEXT NOT NULL,
    detected_at TEXT NOT NULL DEFAULT (datetime('now')),
    resolution  TEXT
);