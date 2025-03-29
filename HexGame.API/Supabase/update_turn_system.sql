-- Update game table with submitted_turn_player_ids field and set current_turn to 1
ALTER TABLE games 
ADD COLUMN IF NOT EXISTS submitted_turn_player_ids TEXT[] DEFAULT '{}'::TEXT[];

-- Update existing games to use turn 1 instead of 0
UPDATE games
SET current_turn = 1
WHERE current_turn = 0;