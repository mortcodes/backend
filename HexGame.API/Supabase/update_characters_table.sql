-- Add pending move fields to characters table
ALTER TABLE characters 
ADD COLUMN IF NOT EXISTS pending_move_target_q INTEGER,
ADD COLUMN IF NOT EXISTS pending_move_target_r INTEGER,
ADD COLUMN IF NOT EXISTS pending_move_target_character_id UUID;

-- Add index to improve performance of pending move queries
CREATE INDEX IF NOT EXISTS idx_characters_pending_moves
ON characters (pending_move_target_character_id)
WHERE pending_move_target_character_id IS NOT NULL;

-- Comment for the migration
COMMENT ON COLUMN characters.pending_move_target_q IS 'Target Q coordinate for pending character movement';
COMMENT ON COLUMN characters.pending_move_target_r IS 'Target R coordinate for pending character movement';
COMMENT ON COLUMN characters.pending_move_target_character_id IS 'ID of the character at target location for battle creation';