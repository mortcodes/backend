-- Create tables for Hex Game

-- Games table
CREATE TABLE IF NOT EXISTS games (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    number_of_players INT NOT NULL,
    map_size INT NOT NULL,
    current_turn INT NOT NULL DEFAULT 0,
    current_player_index INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    status TEXT NOT NULL DEFAULT 'Created'
);

-- Players table
CREATE TABLE IF NOT EXISTS players (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    game_id UUID NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    player_index INT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Hexes table
CREATE TABLE IF NOT EXISTS hexes (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    game_id UUID NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    q INT NOT NULL,
    r INT NOT NULL,
    s INT NOT NULL,
    is_explored BOOLEAN NOT NULL DEFAULT FALSE,
    terrain_rating INT NOT NULL DEFAULT 1,
    resource_industry INT NOT NULL DEFAULT 0,
    resource_agriculture INT NOT NULL DEFAULT 0,
    resource_building INT NOT NULL DEFAULT 0,
    owner_id UUID REFERENCES players(id),
    explored_by TEXT[] DEFAULT '{}'::TEXT[],
    UNIQUE(game_id, q, r, s)
);

-- Characters table
CREATE TABLE IF NOT EXISTS characters (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    game_id UUID NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    player_id UUID NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    q INT NOT NULL,
    r INT NOT NULL,
    melee INT NOT NULL,
    magic INT NOT NULL,
    diplomacy INT NOT NULL,
    movement_points INT NOT NULL DEFAULT 2,
    max_movement_points INT NOT NULL DEFAULT 2
);

-- Cards table
CREATE TABLE IF NOT EXISTS cards (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    game_id UUID NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    player_id UUID NOT NULL REFERENCES players(id) ON DELETE CASCADE,
    card_type TEXT NOT NULL,
    card_definition_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT NOT NULL,
    effect JSONB NOT NULL DEFAULT '{}'::JSONB
);

-- Battles table
CREATE TABLE IF NOT EXISTS battles (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    game_id UUID NOT NULL REFERENCES games(id) ON DELETE CASCADE,
    attacker_character_id UUID NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    defender_character_id UUID NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    hex_q INT NOT NULL,
    hex_r INT NOT NULL,
    battle_type TEXT NOT NULL DEFAULT 'Melee',
    attacker_score INT NOT NULL DEFAULT 0,
    defender_score INT NOT NULL DEFAULT 0,
    terrain_bonus INT NOT NULL DEFAULT 0,
    winner_id UUID REFERENCES players(id),
    is_completed BOOLEAN NOT NULL DEFAULT FALSE,
    current_player_turn UUID REFERENCES players(id),
    cards_played TEXT[] DEFAULT '{}'::TEXT[],
    player_passed TEXT[] DEFAULT '{}'::TEXT[]
);

-- Create indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_hexes_game_id ON hexes(game_id);
CREATE INDEX IF NOT EXISTS idx_hexes_owner_id ON hexes(owner_id);
CREATE INDEX IF NOT EXISTS idx_characters_game_id ON characters(game_id);
CREATE INDEX IF NOT EXISTS idx_characters_player_id ON characters(player_id);
CREATE INDEX IF NOT EXISTS idx_cards_game_id ON cards(game_id);
CREATE INDEX IF NOT EXISTS idx_cards_player_id ON cards(player_id);
CREATE INDEX IF NOT EXISTS idx_players_game_id ON players(game_id);
CREATE INDEX IF NOT EXISTS idx_battles_game_id ON battles(game_id);
CREATE INDEX IF NOT EXISTS idx_battles_is_completed ON battles(is_completed);

-- Create Row Level Security (RLS) Policies for Supabase
ALTER TABLE games ENABLE ROW LEVEL SECURITY;
ALTER TABLE players ENABLE ROW LEVEL SECURITY;
ALTER TABLE hexes ENABLE ROW LEVEL SECURITY;
ALTER TABLE characters ENABLE ROW LEVEL SECURITY;
ALTER TABLE cards ENABLE ROW LEVEL SECURITY;
ALTER TABLE battles ENABLE ROW LEVEL SECURITY;

-- Create policies to allow service role to perform all operations
CREATE POLICY "Service role can do all on games" ON games FOR ALL USING (auth.role() = 'service_role');
CREATE POLICY "Service role can do all on players" ON players FOR ALL USING (auth.role() = 'service_role');
CREATE POLICY "Service role can do all on hexes" ON hexes FOR ALL USING (auth.role() = 'service_role');
CREATE POLICY "Service role can do all on characters" ON characters FOR ALL USING (auth.role() = 'service_role');
CREATE POLICY "Service role can do all on cards" ON cards FOR ALL USING (auth.role() = 'service_role');
CREATE POLICY "Service role can do all on battles" ON battles FOR ALL USING (auth.role() = 'service_role');