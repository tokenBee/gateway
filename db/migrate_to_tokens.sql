-- Migration: Ensure all columns exist in the traces table
DO $$
BEGIN
    -- Add compression_metadata_json if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'traces' AND column_name = 'compression_metadata_json') THEN
        ALTER TABLE traces ADD COLUMN compression_metadata_json TEXT;
    END IF;

    -- Add original_tokens if it doesn't exist (if you migrated from an older version)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'traces' AND column_name = 'original_tokens') THEN
        ALTER TABLE traces ADD COLUMN original_tokens INT NOT NULL DEFAULT 0;
    END IF;

    -- Add saved_cost_usd if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'traces' AND column_name = 'saved_cost_usd') THEN
        ALTER TABLE traces ADD COLUMN saved_cost_usd DECIMAL(18,6) NOT NULL DEFAULT 0;
    END IF;
END
$$;

-- Verify columns again
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'traces'
ORDER BY ordinal_position;
