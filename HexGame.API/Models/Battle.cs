using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models;

namespace HexGame.API.Models
{
    [Table("battles")]
    public class Battle : BaseModel
    {
        [JsonPropertyName("id")]
        [Column("id")]
        [PrimaryKey("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [JsonPropertyName("game_id")]
        [Column("game_id")]
        public string GameId { get; set; } = string.Empty;
        
        [JsonPropertyName("attacker_character_id")]
        [Column("attacker_character_id")]
        public string AttackerCharacterId { get; set; } = string.Empty;
        
        [JsonPropertyName("defender_character_id")]
        [Column("defender_character_id")]
        public string DefenderCharacterId { get; set; } = string.Empty;
        
        [JsonPropertyName("hex_q")]
        [Column("hex_q")]
        public int HexQ { get; set; }
        
        [JsonPropertyName("hex_r")]
        [Column("hex_r")]
        public int HexR { get; set; }
        
        [JsonPropertyName("battle_type")]
        [Column("battle_type")]
        public BattleType BattleType { get; set; } = BattleType.Melee;
        
        [JsonPropertyName("attacker_score")]
        [Column("attacker_score")]
        public int AttackerScore { get; set; }
        
        [JsonPropertyName("defender_score")]
        [Column("defender_score")]
        public int DefenderScore { get; set; }
        
        [JsonPropertyName("terrain_bonus")]
        [Column("terrain_bonus")]
        public int TerrainBonus { get; set; }
        
        [JsonPropertyName("winner_id")]
        [Column("winner_id")]
        public string? WinnerId { get; set; }
        
        [JsonPropertyName("is_completed")]
        [Column("is_completed")]
        public bool IsCompleted { get; set; } = false;
        
        [JsonPropertyName("attacker_submitted")]
        [Column("attacker_submitted")]
        public bool AttackerSubmitted { get; set; } = false;
        
        [JsonPropertyName("defender_submitted")]
        [Column("defender_submitted")]
        public bool DefenderSubmitted { get; set; } = false;
        
        [JsonPropertyName("attacker_cards_played")]
        [Column("attacker_cards_played")]
        public List<string> AttackerCardsPlayed { get; set; } = new List<string>();
        
        [JsonPropertyName("defender_cards_played")]
        [Column("defender_cards_played")]
        public List<string> DefenderCardsPlayed { get; set; } = new List<string>();
        
        // These are deprecated but kept for backward compatibility
        [JsonPropertyName("current_player_turn")]
        [Column("current_player_turn")]
        public string? CurrentPlayerTurn { get; set; }

        [JsonPropertyName("cards_played")]
        [Column("cards_played")]
        public List<string> CardsPlayed { get; set; } = new List<string>();
        
        [JsonPropertyName("player_passed")]
        [Column("player_passed")]
        public List<string> PlayerPassed { get; set; } = new List<string>();
        
        // Add battle history field to store detailed resolution information
        [JsonPropertyName("battle_history")]
        [Column("battle_history")]
        public string? BattleHistory { get; set; }
        
        // Track which turn this battle was completed in
        [JsonPropertyName("completed_turn")]
        [Column("completed_turn")]
        public int? CompletedTurn { get; set; }
    }

    public enum BattleType
    {
        Melee,
        Magic,
        Diplomacy
    }
}
