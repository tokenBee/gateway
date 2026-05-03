CREATE TABLE IF NOT EXISTS traces (
    id UUID PRIMARY KEY,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    path VARCHAR(255) NOT NULL,
    model VARCHAR(100) NOT NULL,
    provider VARCHAR(50) NOT NULL,
    input_tokens INT NOT NULL,
    output_tokens INT NOT NULL,
    original_tokens INT NOT NULL,
    compressed_tokens INT NOT NULL,
    input_cost_usd DECIMAL(18,6) NOT NULL,
    output_cost_usd DECIMAL(18,6) NOT NULL,
    total_cost_usd DECIMAL(18,6) NOT NULL,
    saved_cost_usd DECIMAL(18,6) NOT NULL,
    latency_ms INT NOT NULL,
    status_code INT NOT NULL,
    was_compressed BOOLEAN NOT NULL,
    is_streaming BOOLEAN NOT NULL,
    user_id VARCHAR(100),
    session_id VARCHAR(100),
    properties_json JSONB,
    request_body TEXT,
    original_request_body TEXT,
    response_body TEXT,
    compression_metadata_json TEXT
);

CREATE INDEX IF NOT EXISTS idx_traces_timestamp ON traces (timestamp);
CREATE INDEX IF NOT EXISTS idx_traces_model ON traces (model);

-- Auth & Keys
CREATE TABLE IF NOT EXISTS api_keys (
    id UUID PRIMARY KEY,
    user_id VARCHAR(100) NOT NULL,
    key_hash VARCHAR(255) NOT NULL,
    key_prefix VARCHAR(16) NOT NULL,
    name VARCHAR(255),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_used_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_api_keys_user_id ON api_keys(user_id);
CREATE INDEX IF NOT EXISTS idx_api_keys_prefix ON api_keys(key_prefix);

-- Subscriptions & Billing
CREATE TABLE IF NOT EXISTS subscriptions (
    id UUID PRIMARY KEY,
    user_id VARCHAR(100) UNIQUE NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'free',
    tokens_this_month BIGINT NOT NULL DEFAULT 0,
    free_tokens_used BIGINT NOT NULL DEFAULT 0,
    stripe_customer_id VARCHAR(255),
    stripe_subscription_id VARCHAR(255),
    current_period_start TIMESTAMPTZ,
    current_period_end TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_subscriptions_user_id ON subscriptions(user_id);

CREATE TABLE IF NOT EXISTS usage_events (
    id UUID PRIMARY KEY,
    user_id VARCHAR(100) NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    request_count INT NOT NULL DEFAULT 1,
    reported_to_stripe BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_usage_events_user_id ON usage_events(user_id);

-- Session + Span Recording (Replay)
CREATE TABLE IF NOT EXISTS sessions (
    id           VARCHAR(255) PRIMARY KEY,
    name         VARCHAR(255),
    agent_type   VARCHAR(100),
    started_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ended_at     TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_sessions_started_at ON sessions(started_at);

CREATE TABLE IF NOT EXISTS spans (
    id             UUID PRIMARY KEY,
    session_id     VARCHAR(255) NOT NULL REFERENCES sessions(id),
    type           VARCHAR(50)  NOT NULL DEFAULT 'LlmCall',
    timestamp      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    duration_ms    INT          NOT NULL DEFAULT 0,
    input_payload  TEXT,
    output_payload TEXT,
    tokens         INT          NOT NULL DEFAULT 0,
    metadata_json  TEXT,
    parent_span_id VARCHAR(255)
);

CREATE INDEX IF NOT EXISTS idx_spans_session_id ON spans(session_id);
CREATE INDEX IF NOT EXISTS idx_spans_timestamp ON spans(timestamp);
