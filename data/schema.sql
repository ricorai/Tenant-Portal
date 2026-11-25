-- ========================================================================
-- RENTAL WI-FI CAPTIVE GATEWAY & SUBSCRIPTION MANAGER
-- Database Schema: SQLite 3
-- Target Host: ThinkPad L520 Server Node (Windows 11 / .NET 4.8 / SQLite)
-- Subnet: 192.168.10.0/24 (Tenda F3 AP upstairs, Units 1-4)
-- Rate: ₱150.00 / 30 Days per device
-- ========================================================================

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

-- ------------------------------------------------------------------------
-- Table: leases
-- Authoritative active/pending leases keyed by client MAC address
-- ------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS leases (
    mac_address             TEXT PRIMARY KEY COLLATE NOCASE,   -- Normalized lowercase (e.g., 'e4:5f:01:8b:22:91')
    unit_number             INTEGER NOT NULL CHECK(unit_number BETWEEN 1 AND 4),
    pin_code                TEXT NOT NULL,                     -- 4-digit random recovery PIN (e.g., '4821')
    status                  TEXT NOT NULL DEFAULT 'EXPIRED' 
                            CHECK(status IN ('ACTIVE', 'PENDING', 'EXPIRED', 'EMERGENCY_PASS')),
    paid_reference          TEXT,                              -- 13-digit GCash Reference # (or 'CASH-APPROVE-...')
    amount_paid             REAL NOT NULL DEFAULT 200.00,
    activated_at            DATETIME,                          -- ISO 8601 UTC timestamp
    expires_at              DATETIME,                          -- ISO 8601 UTC (exact 30 days from activation, non-pausable)
    emergency_passes_today  INTEGER NOT NULL DEFAULT 0,        -- Max 1 attempt per day
    last_emergency_pass_at  DATETIME,                          -- Expiration or timestamp of current/last emergency pass
    pin_swaps_this_month    INTEGER NOT NULL DEFAULT 0,        -- Max 2 swaps per billing cycle to mitigate MAC randomization
    created_at              DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at              DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_leases_status ON leases(status);
CREATE INDEX IF NOT EXISTS idx_leases_pin ON leases(pin_code);
CREATE INDEX IF NOT EXISTS idx_leases_unit ON leases(unit_number);

-- ------------------------------------------------------------------------
-- Table: payment_logs
-- Historical audit trail for GCash and Cash approvals
-- ------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS payment_logs (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    mac_address             TEXT NOT NULL COLLATE NOCASE,
    unit_number             INTEGER NOT NULL,
    payment_type            TEXT NOT NULL DEFAULT 'GCASH' CHECK(payment_type IN ('GCASH', 'CASH')),
    paid_reference          TEXT NOT NULL,
    amount                  REAL NOT NULL DEFAULT 200.00,
    status                  TEXT NOT NULL DEFAULT 'PENDING' CHECK(status IN ('PENDING', 'APPROVED', 'REJECTED')),
    submitted_at            DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    approved_at             DATETIME,
    approved_by             TEXT DEFAULT 'MOM_ADMIN',
    notes                   TEXT
);

CREATE INDEX IF NOT EXISTS idx_payment_status ON payment_logs(status);
CREATE INDEX IF NOT EXISTS idx_payment_ref ON payment_logs(paid_reference);

-- ------------------------------------------------------------------------
-- Table: system_settings
-- Low-authority key-value table for gateway config
-- ------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS system_settings (
    key                     TEXT PRIMARY KEY,
    value                   TEXT NOT NULL,
    updated_at              DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

INSERT OR IGNORE INTO system_settings (key, value) VALUES
    ('rate_monthly_php', '200.00'),
    ('sqm_download_mbps', '30'),
    ('sqm_upload_mbps', '15'),
    ('emergency_pass_minutes', '10'),
    ('emergency_max_daily', '1'),
    ('emergency_speed_kbps', '1024'),
    ('admin_pin', '1234'),
    ('gcash_name', 'Rico R.'),
    ('gcash_number', '0917-XXX-XXXX');
