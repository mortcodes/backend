-- Update the battles table to match recent code changes
-- This script adds attacker_submitted, defender_submitted, and card tracking fields

-- First, check if columns exist and add if they don't
DO $$
BEGIN
    -- Add attacker_submitted column if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'battles' AND column_name = 'attacker_submitted'
    ) THEN
        ALTER TABLE battles ADD COLUMN attacker_submitted BOOLEAN NOT NULL DEFAULT FALSE;
    END IF;

    -- Add defender_submitted column if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'battles' AND column_name = 'defender_submitted'
    ) THEN
        ALTER TABLE battles ADD COLUMN defender_submitted BOOLEAN NOT NULL DEFAULT FALSE;
    END IF;

    -- Add attacker_cards_played column if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'battles' AND column_name = 'attacker_cards_played'
    ) THEN
        ALTER TABLE battles ADD COLUMN attacker_cards_played TEXT[] DEFAULT '{}'::TEXT[];
    END IF;

    -- Add defender_cards_played column if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'battles' AND column_name = 'defender_cards_played'
    ) THEN
        ALTER TABLE battles ADD COLUMN defender_cards_played TEXT[] DEFAULT '{}'::TEXT[];
    END IF;

    -- Update participant_player_ids column in games table if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'games' AND column_name = 'participant_player_ids'
    ) THEN
        ALTER TABLE games ADD COLUMN participant_player_ids TEXT[] DEFAULT '{}'::TEXT[];
    END IF;
    
    -- Fix missing columns in cards table
    -- Add pending_resolution column if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'cards' AND column_name = 'pending_resolution'
    ) THEN
        ALTER TABLE cards ADD COLUMN pending_resolution BOOLEAN NOT NULL DEFAULT FALSE;
    END IF;
    
    -- Add target_id column if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'cards' AND column_name = 'target_id'
    ) THEN
        ALTER TABLE cards ADD COLUMN target_id TEXT DEFAULT NULL;
    END IF;
    
    -- Add played_on_turn column if it doesn't exist
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'cards' AND column_name = 'played_on_turn'
    ) THEN
        ALTER TABLE cards ADD COLUMN played_on_turn INTEGER DEFAULT NULL;
    END IF;

    -- Add RLS policies for the new columns
    -- Note: These are already covered by the general RLS policies in the original setup
END
$$;

-- Create any additional needed indexes
CREATE INDEX IF NOT EXISTS idx_battle_attacker_submitted ON battles(attacker_submitted);
CREATE INDEX IF NOT EXISTS idx_battle_defender_submitted ON battles(defender_submitted);
CREATE INDEX IF NOT EXISTS idx_games_status ON games(status);
CREATE INDEX IF NOT EXISTS idx_games_participant_player_ids ON games(participant_player_ids);
CREATE INDEX IF NOT EXISTS idx_cards_pending_resolution ON cards(pending_resolution);
CREATE INDEX IF NOT EXISTS idx_cards_played_on_turn ON cards(played_on_turn);

-- Log migration completion
DO $$
BEGIN
    RAISE NOTICE 'Database update completed successfully';
END
$$;