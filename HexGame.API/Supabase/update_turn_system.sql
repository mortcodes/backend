-- Add battle_history column to battles table
ALTER TABLE battles ADD COLUMN IF NOT EXISTS battle_history TEXT DEFAULT NULL;