-- ============================================================================
-- Mermer ERP — PostgreSQL Database Schema
-- Migration from Couchbase (NoSQL) to PostgreSQL (Relational)
-- Version: 1.8.1 | Core + Licensing (Optimized Indexes & Fixes)
-- ============================================================================

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";

-- ============================================================================
-- LICENSING & ACTIVATION (Priority 0 — Security & Audit)
-- ============================================================================

DROP TABLE IF EXISTS license_logs CASCADE;
DROP TABLE IF EXISTS licenses CASCADE;

CREATE TABLE licenses (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    key             VARCHAR(255) NOT NULL UNIQUE,
    application_id  VARCHAR(100) NOT NULL DEFAULT '55ddc105-8f48-4f78-b214-aea448d2a370',
    module_id       VARCHAR(100) NOT NULL,
    machine_id      VARCHAR(255),
    note            TEXT,
    valid_from      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    valid_till      TIMESTAMPTZ,
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_licenses_key ON licenses(key);
CREATE INDEX idx_licenses_app_mod ON licenses(application_id, module_id);

CREATE TABLE license_logs (
    id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    license_key VARCHAR(255),
    machine_id  VARCHAR(255),
    action      VARCHAR(50),
    status      VARCHAR(50),
    details     TEXT,
    ip_address  VARCHAR(100),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_license_logs_key ON license_logs(license_key);
CREATE INDEX idx_license_logs_action ON license_logs(action);
CREATE INDEX idx_license_logs_created_at ON license_logs(created_at DESC);

-- ============================================================================
-- REPORT LAYOUTS (DevExpress)
-- ============================================================================

CREATE TABLE IF NOT EXISTS report_layouts (
    id          VARCHAR(150) PRIMARY KEY,
    name        VARCHAR(150) NOT NULL,
    layout      TEXT NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_report_layouts_name ON report_layouts(name);

COMMENT ON TABLE report_layouts IS 'Пользовательские XML-макеты печатных форм DevExpress';
COMMENT ON COLUMN report_layouts.id IS 'Уникальный строковый ключ формы вида Report-{ClassName}';
COMMENT ON COLUMN report_layouts.layout IS 'XML-разметка сериализованного отчета XtraReport';

-- ============================================================================
-- REFERENCE TABLES (Priority 1 — Core)
-- ============================================================================

CREATE TABLE offices (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name            VARCHAR(200) NOT NULL,
    region          VARCHAR(200),
    description     TEXT,
    tags            TEXT[],
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE warehouses (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    office_id       UUID REFERENCES offices(id),
    name            VARCHAR(200) NOT NULL,
    description     TEXT,
    tags            TEXT[],
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_warehouses_office_id ON warehouses(office_id);

CREATE TABLE depositories (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    office_id       UUID REFERENCES offices(id),
    name            VARCHAR(200) NOT NULL,
    description     TEXT,
    tags            TEXT[],
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_depositories_office_id ON depositories(office_id);

CREATE TABLE currencies (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name            VARCHAR(100) NOT NULL,
    decimals        INT NOT NULL DEFAULT 2,
    is_default      BOOLEAN NOT NULL DEFAULT FALSE,
    description     TEXT,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE currency_rates (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    currency_id     UUID NOT NULL REFERENCES currencies(id) ON DELETE CASCADE,
    valid_from      DATE NOT NULL DEFAULT CURRENT_DATE,
    multiplier      NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider         NUMERIC(18,8) NOT NULL DEFAULT 1,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chk_currency_rate_divider_nonzero CHECK (divider != 0),
    CONSTRAINT chk_currency_rate_multiplier_nonzero CHECK (multiplier != 0)
);

CREATE INDEX idx_currency_rates_currency_id ON currency_rates(currency_id);
CREATE INDEX idx_currency_rates_valid_from ON currency_rates(currency_id, valid_from DESC);

-- ============================================================================
-- USERS & AUTHORIZATION
-- ============================================================================

CREATE TABLE users (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    username        VARCHAR(100) NOT NULL UNIQUE,
    password        VARCHAR(500) NOT NULL DEFAULT '',
    is_admin        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    description     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE roles (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name            VARCHAR(200) NOT NULL,
    description     TEXT,
    authorizations  TEXT,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE user_roles (
    user_id         UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_id         UUID NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);

-- ============================================================================
-- CRM — Partners & Partner Actions
-- ============================================================================

CREATE TABLE partners (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code            VARCHAR(50),
    name            VARCHAR(500) NOT NULL,
    phone           VARCHAR(100),
    address         TEXT,
    group_name      VARCHAR(200),
    credit_limit    NUMERIC(18,4),
    tags            TEXT[],
    description     TEXT,
    rating          NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_partners_code ON partners(code);
CREATE INDEX idx_partners_name_trgm ON partners USING GIN (name gin_trgm_ops);

CREATE TABLE partner_slips (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code            VARCHAR(50) NOT NULL,
    date            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    slip_type       VARCHAR(50) NOT NULL,
    office_id       UUID REFERENCES offices(id),
    user_id         UUID REFERENCES users(id),
    user_name       VARCHAR(200),
    is_completed    BOOLEAN NOT NULL DEFAULT TRUE,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    group_name      VARCHAR(200),
    tags            TEXT[],
    description     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_partner_slips_date ON partner_slips(date DESC);
CREATE INDEX idx_partner_slips_office_date ON partner_slips(office_id, date DESC);

CREATE TABLE partner_slip_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    partner_slip_id     UUID NOT NULL REFERENCES partner_slips(id) ON DELETE CASCADE,
    partner_id          UUID REFERENCES partners(id),
    debit_amount        NUMERIC(18,4) NOT NULL DEFAULT 0,
    debit_currency_id   UUID REFERENCES currencies(id),
    credit_amount       NUMERIC(18,4) NOT NULL DEFAULT 0,
    credit_currency_id  UUID REFERENCES currencies(id)
);

CREATE INDEX idx_partner_slip_lines_slip_id ON partner_slip_lines(partner_slip_id);
CREATE INDEX idx_partner_slip_lines_partner_id ON partner_slip_lines(partner_id);

CREATE TABLE partner_transfers (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code            VARCHAR(50) NOT NULL,
    date            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    user_id         UUID REFERENCES users(id),
    user_name       VARCHAR(200),
    is_completed    BOOLEAN NOT NULL DEFAULT TRUE,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    group_name      VARCHAR(200),
    tags            TEXT[],
    description     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_partner_transfers_date ON partner_transfers(date DESC);

CREATE TABLE partner_transfer_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    partner_transfer_id UUID NOT NULL REFERENCES partner_transfers(id) ON DELETE CASCADE,
    office_id           UUID REFERENCES offices(id),
    partner_id          UUID REFERENCES partners(id),
    debit_amount        NUMERIC(18,4) NOT NULL DEFAULT 0,
    debit_currency_id   UUID REFERENCES currencies(id),
    credit_amount       NUMERIC(18,4) NOT NULL DEFAULT 0,
    credit_currency_id  UUID REFERENCES currencies(id)
);

CREATE INDEX idx_partner_transfer_lines_transfer_id ON partner_transfer_lines(partner_transfer_id);
CREATE INDEX idx_partner_transfer_lines_partner_id ON partner_transfer_lines(partner_id);

-- Таблица взаиморасчетов перенесена сюда, строго до создания ее индексов
CREATE TABLE IF NOT EXISTS partner_actions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    partner_id      UUID NOT NULL REFERENCES partners(id) ON DELETE CASCADE,
    office_id       UUID REFERENCES offices(id),
    action_type     VARCHAR(20) NOT NULL CHECK (action_type IN ('Debit','Credit')),
    amount          NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    description     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_partner_actions_partner_id ON partner_actions(partner_id);
CREATE INDEX idx_partner_actions_office_id  ON partner_actions(office_id);
CREATE INDEX idx_partner_actions_partner_date ON partner_actions(partner_id, created_at DESC);

-- ============================================================================
-- STOCK MANAGEMENT
-- ============================================================================

CREATE TABLE stocks (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code            VARCHAR(50),
    name            VARCHAR(500) NOT NULL,
    short_name      VARCHAR(200),
    type            VARCHAR(100),
    group_name      VARCHAR(200),
    tags            TEXT[],
    barcodes        TEXT[],
    limit_min       NUMERIC(18,4),
    limit_max       NUMERIC(18,4),
    search_vector   tsvector,
    description     TEXT,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX idx_stocks_code ON stocks(code) WHERE code IS NOT NULL;
CREATE INDEX idx_stocks_name_trgm ON stocks USING GIN (name gin_trgm_ops);
CREATE INDEX idx_stocks_code_trgm ON stocks USING GIN (code gin_trgm_ops) WHERE code IS NOT NULL;
CREATE INDEX idx_stocks_barcodes ON stocks USING GIN (barcodes);
CREATE INDEX idx_stocks_is_disabled ON stocks(is_disabled) WHERE NOT is_disabled;
CREATE INDEX IF NOT EXISTS ix_stocks_search_vector ON stocks USING GIN(search_vector);
CREATE INDEX IF NOT EXISTS ix_stocks_name_prefix ON stocks (LOWER(name) varchar_pattern_ops);
CREATE INDEX IF NOT EXISTS ix_stocks_code_prefix ON stocks (LOWER(code) varchar_pattern_ops);


CREATE TABLE stock_units (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_id        UUID NOT NULL REFERENCES stocks(id) ON DELETE CASCADE,
    name            VARCHAR(100) NOT NULL,
    multiplier      NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider         NUMERIC(18,8) NOT NULL DEFAULT 1,
    is_default      BOOLEAN NOT NULL DEFAULT FALSE,

    CONSTRAINT chk_stock_unit_divider_nonzero CHECK (divider != 0),
    CONSTRAINT chk_stock_unit_multiplier_nonzero CHECK (multiplier != 0)
);

CREATE INDEX idx_stock_units_stock_id ON stock_units(stock_id);
-- >>> ДОБАВЛЕНО СЮДА: быстрый поиск дефолтной единицы для CTE без LATERAL <<<
CREATE INDEX IF NOT EXISTS idx_stock_units_default ON stock_units(stock_id) WHERE is_default = TRUE;

-- Stock Prices
CREATE TABLE stock_prices (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_id        UUID NOT NULL REFERENCES stocks(id) ON DELETE CASCADE,
    valid_from      DATE NOT NULL DEFAULT CURRENT_DATE,
    price           NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    price_group     VARCHAR(100),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_prices_stock_id ON stock_prices(stock_id);
CREATE INDEX idx_stock_prices_lookup ON stock_prices(stock_id, price_group, valid_from DESC);
-- >>> ДОБАВЛЕНО СЮДА: быстрый DISTINCT ON (stock_id) valid_from DESC <<<
CREATE INDEX IF NOT EXISTS idx_stock_prices_distinct ON stock_prices(stock_id, valid_from DESC);, price_group, valid_from DESC);

CREATE TABLE stock_additional_prices (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_id        UUID NOT NULL REFERENCES stocks(id) ON DELETE CASCADE,
    price           NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    price_group     VARCHAR(100),
    valid_from      DATE NOT NULL DEFAULT CURRENT_DATE
);

CREATE INDEX idx_stock_additional_prices_stock_id ON stock_additional_prices(stock_id);

CREATE TABLE stock_name_composers (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    "order"         INT NOT NULL DEFAULT 0,
    name            VARCHAR(500) NOT NULL,
    description     TEXT,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_name_composers_order ON stock_name_composers("order");

CREATE TABLE stock_name_composer_values (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    composer_id     UUID NOT NULL REFERENCES stock_name_composers(id) ON DELETE CASCADE,
    "order"         INT NOT NULL DEFAULT 0,
    name            VARCHAR(500),
    short_name      VARCHAR(200)
);

CREATE INDEX idx_snc_values_composer_id ON stock_name_composer_values(composer_id);

CREATE TABLE stock_alternatives (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name            VARCHAR(500) NOT NULL,
    description     TEXT,
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE stock_alternative_lines (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_alternative_id    UUID NOT NULL REFERENCES stock_alternatives(id) ON DELETE CASCADE,
    stock_id                UUID REFERENCES stocks(id)
);

CREATE INDEX idx_stock_alt_lines_alt_id ON stock_alternative_lines(stock_alternative_id);
CREATE INDEX idx_stock_alt_lines_stock_id ON stock_alternative_lines(stock_id);

-- ============================================================================
-- WAREHOUSING & TRANSACTIONS
-- ============================================================================

CREATE TABLE stock_slips (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    slip_type           VARCHAR(50) NOT NULL,
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_stock_income     BOOLEAN NOT NULL DEFAULT FALSE,
    display_total       NUMERIC(18,4) NOT NULL DEFAULT 0,
    description         TEXT,
    group_name          VARCHAR(200),
    tags                TEXT[],
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    user_id             UUID REFERENCES users(id),
    warehouse_id        UUID REFERENCES warehouses(id),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_slips_date ON stock_slips(date DESC);
CREATE INDEX idx_stock_slips_warehouse_id ON stock_slips(warehouse_id);
CREATE INDEX idx_stock_slips_slip_type ON stock_slips(slip_type);
CREATE INDEX idx_stock_slips_wh_date ON stock_slips(warehouse_id, date DESC);

CREATE TABLE stock_slip_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_slip_id       UUID NOT NULL REFERENCES stock_slips(id) ON DELETE CASCADE,
    stock_id            UUID REFERENCES stocks(id),
    unit_id             UUID REFERENCES stock_units(id),
    quantity            NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_quantity     NUMERIC(18,4) NOT NULL DEFAULT 0,
    price               NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_total        NUMERIC(18,4) NOT NULL DEFAULT 0,
    sort_order          INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_stock_slip_lines_slip_id ON stock_slip_lines(stock_slip_id);
CREATE INDEX idx_stock_slip_lines_stock_id ON stock_slip_lines(stock_id);

CREATE TABLE stock_transfers (
    id                          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                        VARCHAR(50),
    date                        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    warehouse_id                UUID REFERENCES warehouses(id),
    destination_warehouse_id    UUID REFERENCES warehouses(id),
    display_currency_id         UUID REFERENCES currencies(id),
    is_completed                BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled                 BOOLEAN NOT NULL DEFAULT FALSE,
    user_name                   VARCHAR(100),
    group_name                  VARCHAR(200),
    description                 TEXT,
    tags                        TEXT[],
    action_total                NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_received_total       NUMERIC(18,4) NOT NULL DEFAULT 0,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_transfers_date ON stock_transfers(date DESC);
CREATE INDEX idx_stock_transfers_wh ON stock_transfers(warehouse_id);
CREATE INDEX idx_stock_transfers_dest_wh ON stock_transfers(destination_warehouse_id);
CREATE INDEX idx_stock_transfers_wh_date ON stock_transfers(warehouse_id, date DESC);

CREATE TABLE stock_transfer_lines (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_transfer_id       UUID NOT NULL REFERENCES stock_transfers(id) ON DELETE CASCADE,
    stock_id                UUID REFERENCES stocks(id),
    unit_id                 UUID REFERENCES stock_units(id),
    received_unit_id        UUID REFERENCES stock_units(id),
    quantity                NUMERIC(18,4) NOT NULL DEFAULT 0,
    received_quantity       NUMERIC(18,4) NOT NULL DEFAULT 0,
    price                   NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_total            NUMERIC(18,4) NOT NULL DEFAULT 0,
    action_received_total   NUMERIC(18,4) NOT NULL DEFAULT 0,
    sort_order              INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_stock_transfer_lines_doc ON stock_transfer_lines(stock_transfer_id);
CREATE INDEX idx_stock_transfer_lines_stock ON stock_transfer_lines(stock_id);

CREATE TABLE stock_revisions (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    finish_date         TIMESTAMPTZ,
    warehouse_id        UUID REFERENCES warehouses(id),
    exceed_slip_id      UUID,
    deficit_slip_id     UUID,
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(100),
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    group_name          VARCHAR(100),
    tags                TEXT[],
    description         TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_revisions_date ON stock_revisions(date DESC);
CREATE INDEX idx_stock_revisions_wh ON stock_revisions(warehouse_id);
CREATE INDEX idx_stock_revisions_wh_date ON stock_revisions(warehouse_id, date DESC);

CREATE TABLE stock_revision_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_revision_id   UUID NOT NULL REFERENCES stock_revisions(id) ON DELETE CASCADE,
    stock_id            UUID REFERENCES stocks(id),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    quantity            NUMERIC(18,4) NOT NULL DEFAULT 0,
    unit_id             UUID REFERENCES stock_units(id),
    price               NUMERIC(18,4),
    currency_id         UUID REFERENCES currencies(id),
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(100),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_revision_lines_rev_id ON stock_revision_lines(stock_revision_id);
CREATE INDEX idx_stock_revision_lines_stock_id ON stock_revision_lines(stock_id);

CREATE TABLE stock_orders (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    warehouse_id        UUID REFERENCES warehouses(id),
    partner_id          UUID REFERENCES partners(id),
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(200),
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    group_name          VARCHAR(200),
    tags                TEXT[],
    description         TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_stock_orders_date ON stock_orders(date DESC);
CREATE INDEX idx_stock_orders_warehouse ON stock_orders(warehouse_id);
CREATE INDEX idx_stock_orders_partner ON stock_orders(partner_id);
CREATE INDEX idx_stock_orders_wh_date ON stock_orders(warehouse_id, date DESC);

CREATE TABLE stock_order_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_order_id      UUID NOT NULL REFERENCES stock_orders(id) ON DELETE CASCADE,
    stock_id            UUID REFERENCES stocks(id),
    quantity            NUMERIC(18,4) NOT NULL DEFAULT 0,
    unit_id             UUID REFERENCES stock_units(id)
);

CREATE INDEX idx_stock_order_lines_order_id ON stock_order_lines(stock_order_id);
CREATE INDEX idx_stock_order_lines_stock_id ON stock_order_lines(stock_id);

CREATE TABLE stock_order_unit_convertions (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_order_id      UUID NOT NULL REFERENCES stock_orders(id) ON DELETE CASCADE,
    stock_id            UUID REFERENCES stocks(id),
    unit_id             UUID REFERENCES stock_units(id),
    multiplier          NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider             NUMERIC(18,8) NOT NULL DEFAULT 1
);

CREATE INDEX idx_stock_order_conv_order_id ON stock_order_unit_convertions(stock_order_id);

CREATE TABLE stock_order_templates (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name                VARCHAR(500) NOT NULL,
    group_name          VARCHAR(200),
    tags                TEXT[],
    description         TEXT,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE stock_order_template_lines (
    id                      UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    stock_order_template_id UUID NOT NULL REFERENCES stock_order_templates(id) ON DELETE CASCADE,
    stock_id                UUID REFERENCES stocks(id)
);

CREATE INDEX idx_sot_lines_template_id ON stock_order_template_lines(stock_order_template_id);
CREATE INDEX idx_sot_lines_stock_id ON stock_order_template_lines(stock_id);

CREATE TABLE aggregated_stock_orders (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    warehouse_id        UUID REFERENCES warehouses(id),
    partner_id          UUID REFERENCES partners(id),
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(200),
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    group_name          VARCHAR(200),
    tags                TEXT[],
    description         TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_agg_orders_date ON aggregated_stock_orders(date DESC);
CREATE INDEX idx_agg_orders_wh ON aggregated_stock_orders(warehouse_id);
CREATE INDEX idx_agg_orders_partner ON aggregated_stock_orders(partner_id);

CREATE TABLE aggregated_stock_order_lines (
    id                          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    aggregated_stock_order_id   UUID NOT NULL REFERENCES aggregated_stock_orders(id) ON DELETE CASCADE,
    stock_id                    UUID REFERENCES stocks(id),
    unit_id                     UUID REFERENCES stock_units(id),
    orders                      JSONB NOT NULL DEFAULT '{}'
);

CREATE INDEX idx_agg_order_lines_order_id ON aggregated_stock_order_lines(aggregated_stock_order_id);
CREATE INDEX idx_agg_order_lines_stock_id ON aggregated_stock_order_lines(stock_id);

-- ============================================================================
-- COMMERCE — Invoices
-- ============================================================================

CREATE TABLE invoices (
    id                       UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                     VARCHAR(50),
    date                     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    due_date                 TIMESTAMPTZ,
    invoice_type             VARCHAR(20) NOT NULL CHECK (invoice_type IN ('Purchase', 'PurchaseReturn', 'Sales', 'SalesReturn')),
    user_id                  UUID REFERENCES users(id),
    user_name                VARCHAR(200),
    office_id                UUID REFERENCES offices(id),
    warehouse_id             UUID REFERENCES warehouses(id),
    depository_id            UUID REFERENCES depositories(id),
    partner_id               UUID REFERENCES partners(id),
    display_currency_id      UUID REFERENCES currencies(id),
    stock_price_group        VARCHAR(100),
    debit_credit_left_amount BOOLEAN NOT NULL DEFAULT FALSE,
    is_completed             BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled              BOOLEAN NOT NULL DEFAULT FALSE,
    group_name               VARCHAR(200),
    tags                     TEXT[],
    description              TEXT,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_invoices_date ON invoices(date DESC);
CREATE INDEX idx_invoices_partner_id ON invoices(partner_id);
CREATE INDEX idx_invoices_warehouse_id ON invoices(warehouse_id);
CREATE INDEX idx_invoices_type ON invoices(invoice_type);
CREATE INDEX idx_invoices_code ON invoices(code);
-- Добавлен критичный составной индекс по филиалу и дате
CREATE INDEX idx_invoices_office_date ON invoices(office_id, date DESC);
CREATE INDEX idx_invoices_partner_date ON invoices(partner_id, date DESC);
CREATE INDEX idx_invoices_type_date ON invoices(invoice_type, date DESC);
CREATE INDEX idx_invoices_wh_date ON invoices(warehouse_id, date DESC);
CREATE INDEX idx_invoices_active_date ON invoices(date DESC) WHERE is_disabled = FALSE;

CREATE TABLE invoice_lines (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_id      UUID NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    source_id       UUID,
    stock_id        UUID REFERENCES stocks(id),
    unit_id         UUID REFERENCES stock_units(id),
    quantity        NUMERIC(18,4) NOT NULL DEFAULT 0,
    price           NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    sort_order      INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_invoice_lines_invoice_id ON invoice_lines(invoice_id);
CREATE INDEX idx_invoice_lines_stock_id ON invoice_lines(stock_id);
CREATE INDEX idx_invoice_lines_source_id ON invoice_lines(source_id) WHERE source_id IS NOT NULL;

CREATE TABLE invoice_currency_convertions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_id      UUID NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    currency_id     UUID NOT NULL REFERENCES currencies(id),
    multiplier      NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider         NUMERIC(18,8) NOT NULL DEFAULT 1,

    CONSTRAINT chk_inv_cc_divider_nonzero CHECK (divider != 0),
    CONSTRAINT chk_inv_cc_multiplier_nonzero CHECK (multiplier != 0)
);

CREATE INDEX idx_inv_cc_invoice_id ON invoice_currency_convertions(invoice_id);

CREATE TABLE invoice_stock_unit_convertions (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_id      UUID NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    stock_id        UUID NOT NULL REFERENCES stocks(id),
    unit_id         UUID NOT NULL REFERENCES stock_units(id),
    multiplier      NUMERIC(18,8) NOT NULL DEFAULT 1,
    divider         NUMERIC(18,8) NOT NULL DEFAULT 1,

    CONSTRAINT chk_inv_suc_divider_nonzero CHECK (divider != 0),
    CONSTRAINT chk_inv_suc_multiplier_nonzero CHECK (multiplier != 0)
);

CREATE INDEX idx_inv_suc_invoice_id ON invoice_stock_unit_convertions(invoice_id);

CREATE TABLE invoice_discounts (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_id      UUID NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    discount_type   VARCHAR(20) NOT NULL CHECK (discount_type IN ('Flat', 'Percentage')),
    amount          NUMERIC(18,4) NOT NULL DEFAULT 0,
    description     TEXT,
    sort_order      INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_invoice_discounts_invoice_id ON invoice_discounts(invoice_id);

CREATE TABLE invoice_payments (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_id      UUID NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    payment_type    VARCHAR(20) NOT NULL DEFAULT 'Payment' CHECK (payment_type IN ('Payment', 'Change')),
    amount          NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    sort_order      INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_invoice_payments_invoice_id ON invoice_payments(invoice_id);

CREATE TABLE invoice_overheads (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_id      UUID NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    amount          NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    description     TEXT,
    sort_order      INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_invoice_overheads_invoice_id ON invoice_overheads(invoice_id);

-- ============================================================================
-- STOCK BALANCES
-- ============================================================================

CREATE TABLE stock_balances (
    warehouse_id    UUID NOT NULL REFERENCES warehouses(id),
    stock_id        UUID NOT NULL REFERENCES stocks(id),
    income          NUMERIC(18,4) NOT NULL DEFAULT 0,
    expense         NUMERIC(18,4) NOT NULL DEFAULT 0,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    PRIMARY KEY (warehouse_id, stock_id)
);

CREATE INDEX idx_stock_balances_stock_id ON stock_balances(stock_id);
CREATE INDEX idx_stock_balances_warehouse_id ON stock_balances(warehouse_id);

-- ============================================================================
-- FUNDS MANAGEMENT
-- ============================================================================

CREATE TABLE funds_slips (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    funds_slip_type     VARCHAR(50) NOT NULL,
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(200),
    office_id           UUID REFERENCES offices(id),
    depository_id       UUID REFERENCES depositories(id),
    partner_id          UUID REFERENCES partners(id),
    display_currency_id UUID REFERENCES currencies(id),
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    group_name          VARCHAR(200),
    tags                TEXT[],
    description         TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_funds_slips_date ON funds_slips(date DESC);
CREATE INDEX idx_funds_slips_partner ON funds_slips(partner_id);
CREATE INDEX idx_funds_slips_dep_date ON funds_slips(depository_id, date DESC);
CREATE INDEX idx_funds_slips_office_date ON funds_slips(office_id, date DESC);

CREATE TABLE funds_slip_lines (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    funds_slip_id   UUID NOT NULL REFERENCES funds_slips(id) ON DELETE CASCADE,
    amount          NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id     UUID REFERENCES currencies(id),
    sort_order      INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_funds_slip_lines_slip_id ON funds_slip_lines(funds_slip_id);

CREATE TABLE funds_transfers (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(200),
    from_depository_id  UUID REFERENCES depositories(id),
    to_depository_id    UUID REFERENCES depositories(id),
    display_currency_id UUID REFERENCES currencies(id),
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    group_name          VARCHAR(200),
    tags                TEXT[],
    description         TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_funds_transfers_date ON funds_transfers(date DESC);
CREATE INDEX idx_funds_transfers_from_dep ON funds_transfers(from_depository_id);
CREATE INDEX idx_funds_transfers_to_dep ON funds_transfers(to_depository_id);

CREATE TABLE funds_transfer_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    funds_transfer_id   UUID NOT NULL REFERENCES funds_transfers(id) ON DELETE CASCADE,
    amount              NUMERIC(18,4) NOT NULL DEFAULT 0,
    received_amount     NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id         UUID REFERENCES currencies(id),
    sort_order          INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_funds_transfer_lines_transfer_id ON funds_transfer_lines(funds_transfer_id);

-- ============================================================================
-- EXPENSES & REGISTRIES
-- ============================================================================

CREATE TABLE expenses (
    id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name            VARCHAR(255) NOT NULL,
    type            VARCHAR(100),
    group_name      VARCHAR(200),
    description     TEXT,
    tags            TEXT[],
    is_disabled     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_expenses_name_trgm ON expenses USING GIN (name gin_trgm_ops);
CREATE INDEX idx_expenses_group_name ON expenses(group_name);
CREATE INDEX idx_expenses_is_disabled ON expenses(is_disabled) WHERE NOT is_disabled;

CREATE TABLE expense_slips (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(200),
    office_id           UUID REFERENCES offices(id),
    depository_id       UUID REFERENCES depositories(id),
    display_currency_id UUID REFERENCES currencies(id),
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    group_name          VARCHAR(200),
    tags                TEXT[],
    description         TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_expense_slips_date ON expense_slips(date DESC);
CREATE INDEX idx_expense_slips_office_date ON expense_slips(office_id, date DESC);

CREATE TABLE expense_slip_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    expense_slip_id     UUID NOT NULL REFERENCES expense_slips(id) ON DELETE CASCADE,
    expense_id          UUID REFERENCES expenses(id),
    amount              NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id         UUID REFERENCES currencies(id),
    sort_order          INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_expense_slip_lines_slip_id ON expense_slip_lines(expense_slip_id);

CREATE TABLE daily_funds_registeries (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    code                VARCHAR(50),
    date                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    user_id             UUID REFERENCES users(id),
    user_name           VARCHAR(200),
    depository_id       UUID REFERENCES depositories(id),
    display_currency_id UUID REFERENCES currencies(id),
    is_completed        BOOLEAN NOT NULL DEFAULT FALSE,
    is_disabled         BOOLEAN NOT NULL DEFAULT FALSE,
    group_name          VARCHAR(200),
    tags                TEXT[],
    description         TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE daily_funds_registery_lines (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    registery_id        UUID NOT NULL REFERENCES daily_funds_registeries(id) ON DELETE CASCADE,
    amount              NUMERIC(18,4) NOT NULL DEFAULT 0,
    currency_id         UUID REFERENCES currencies(id),
    sort_order          INT NOT NULL DEFAULT 0
);

CREATE INDEX idx_daily_funds_reg_date ON daily_funds_registeries(date DESC);
CREATE INDEX idx_daily_funds_reg_dep ON daily_funds_registeries(depository_id);
CREATE INDEX idx_daily_funds_reg_lines_reg_id ON daily_funds_registery_lines(registery_id);

-- ============================================================================
-- MATERIALIZED VIEW: Stock Search
-- ============================================================================

CREATE MATERIALIZED VIEW mv_stock_search AS
SELECT
    s.id,
    s.code,
    s.name,
    s.short_name,
    s.barcodes,
    s.is_disabled,
    su.id        AS unit_id,
    su.name      AS unit_name,
    sp.price,
    sp.currency_id,
    c.name       AS currency_name
FROM stocks s
LEFT JOIN stock_units su ON su.stock_id = s.id AND su.is_default = TRUE
LEFT JOIN LATERAL (
    SELECT sp2.price, sp2.currency_id
    FROM stock_prices sp2
    WHERE sp2.stock_id = s.id AND sp2.price_group IS NULL
    ORDER BY sp2.valid_from DESC
    LIMIT 1
) sp ON TRUE
LEFT JOIN currencies c ON c.id = sp.currency_id
WHERE NOT s.is_disabled;

CREATE UNIQUE INDEX idx_mv_stock_search_id ON mv_stock_search(id);
CREATE INDEX idx_mv_stock_search_name_trgm ON mv_stock_search USING GIN (name gin_trgm_ops);
CREATE INDEX idx_mv_stock_search_code_trgm ON mv_stock_search USING GIN (code gin_trgm_ops) WHERE code IS NOT NULL;

-- ============================================================================
-- UPDATED_AT TRIGGER (auto-update timestamp)
-- ============================================================================

CREATE OR REPLACE FUNCTION fn_update_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DO $$
DECLARE
    tbl TEXT;
BEGIN
    FOR tbl IN
        SELECT c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t 
          ON c.table_name = t.table_name 
         AND c.table_schema = t.table_schema
        WHERE c.table_schema = 'public'
          AND c.column_name = 'updated_at'
          AND t.table_type = 'BASE TABLE'
    LOOP
        EXECUTE format(
            'CREATE TRIGGER trg_%s_updated_at BEFORE UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION fn_update_timestamp()',
            tbl, tbl
        );
    END LOOP;
END;
$$;