using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models;

namespace HexGame.API.Models
{
    [Table("cards")]
    public class Card : BaseModel
    {
        [JsonPropertyName("id")]
        [Column("id")]
        [PrimaryKey("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [JsonPropertyName("game_id")]
        [Column("game_id")]
        public string GameId { get; set; } = string.Empty;
        
        [JsonPropertyName("player_id")]
        [Column("player_id")]
        public string PlayerId { get; set; } = string.Empty;
        
        [JsonPropertyName("card_type")]
        [Column("card_type")]
        public CardType CardType { get; set; }
        
        [JsonPropertyName("card_definition_id")]
        [Column("card_definition_id")]
        public string CardDefinitionId { get; set; } = string.Empty;
        
        [JsonPropertyName("name")]
        [Column("name")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        [Column("description")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("effect")]
        [Column("effect")]
        public CardEffect Effect { get; set; } = new CardEffect();
        
        [JsonPropertyName("pending_resolution")]
        [Column("pending_resolution")]
        public bool PendingResolution { get; set; } = false;
        
        [JsonPropertyName("target_id")]
        [Column("target_id")]
        public string? TargetId { get; set; }
        
        [JsonPropertyName("played_on_turn")]
        [Column("played_on_turn")]
        public int? PlayedOnTurn { get; set; }
    }

    public enum CardType
    {
        Battle,
        General,
        Strategy
    }

    public class CardEffect
    {
        [JsonPropertyName("stat_bonus")]
        public int StatBonus { get; set; } = 0;
        
        [JsonPropertyName("affects_stat")]
        public string? AffectsStat { get; set; }
        
        [JsonPropertyName("negate_terrain")]
        public bool NegateTerrainBonus { get; set; } = false;
        
        [JsonPropertyName("additional_movement")]
        public int AdditionalMovement { get; set; } = 0;
        
        [JsonPropertyName("battle_bonus")]
        public int BattleBonus { get; set; } = 0;
        
        [JsonPropertyName("defensive_bonus")]
        public int DefensiveBonus { get; set; } = 0;
        
        [JsonPropertyName("effect_duration")]
        public int EffectDuration { get; set; } = 1; // Number of turns effect lasts (default is 1)
    }
}
