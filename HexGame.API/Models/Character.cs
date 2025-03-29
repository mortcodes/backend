using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models;

namespace HexGame.API.Models
{
    [Table("characters")]
    public class Character : BaseModel
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
        
        [JsonPropertyName("q")]
        [Column("q")]
        public int Q { get; set; }
        
        [JsonPropertyName("r")]
        [Column("r")]
        public int R { get; set; }
        
        [JsonPropertyName("melee")]
        [Column("melee")]
        public int Melee { get; set; }
        
        [JsonPropertyName("magic")]
        [Column("magic")]
        public int Magic { get; set; }
        
        [JsonPropertyName("diplomacy")]
        [Column("diplomacy")]
        public int Diplomacy { get; set; }
        
        [JsonPropertyName("movement_points")]
        [Column("movement_points")]
        public int MovementPoints { get; set; } = 2;
        
        [JsonPropertyName("max_movement_points")]
        [Column("max_movement_points")]
        public int MaxMovementPoints { get; set; } = 2;
        
        [JsonPropertyName("pending_move_target_q")]
        [Column("pending_move_target_q")]
        public int? PendingMoveTargetQ { get; set; }
        
        [JsonPropertyName("pending_move_target_r")]
        [Column("pending_move_target_r")]
        public int? PendingMoveTargetR { get; set; }
        
        [JsonPropertyName("pending_move_target_character_id")]
        [Column("pending_move_target_character_id")]
        public string? PendingMoveTargetCharacterId { get; set; }
    }
}
