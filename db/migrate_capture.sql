-- Interaction capture, retention, and indexes
-- Safe to run on existing TokenBee databases.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'traces' AND column_name = 'capture_enabled'
    ) THEN
        ALTER TABLE traces ADD COLUMN capture_enabled BOOLEAN NOT NULL DEFAULT TRUE;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'traces' AND column_name = 'expires_at'
    ) THEN
        ALTER TABLE traces ADD COLUMN expires_at TIMESTAMPTZ;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS idx_traces_session_id ON traces(session_id);
CREATE INDEX IF NOT EXISTS idx_traces_provider ON traces(provider);
CREATE INDEX IF NOT EXISTS idx_traces_status_code ON traces(status_code);
CREATE INDEX IF NOT EXISTS idx_traces_expires_at ON traces(expires_at);
CREATE INDEX IF NOT EXISTS idx_traces_account_timestamp ON traces(account_id, timestamp DESC);

CREATE TABLE IF NOT EXISTS capture_settings (
    user_id VARCHAR(100) PRIMARY KEY,
    capture_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    retention_days INT NOT NULL DEFAULT 3,
    capture_messages BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
