using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models;

namespace HexGame.API.Models
{
    [Table("hexes")]
    public class Hex : BaseModel
    {
        [JsonPropertyName("id")]
        [Column("id")]
        [PrimaryKey("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [JsonPropertyName("game_id")]
        [Column("game_id")]
        public string GameId { get; set; } = string.Empty;
        
        [JsonPropertyName("q")]
        [Column("q")]
        public int Q { get; set; }
        
        [JsonPropertyName("r")]
        [Column("r")]
        public int R { get; set; }
        
        [JsonPropertyName("s")]
        [Column("s")]
        public int S { get; set; } // For cube coordinates, S = -Q-R
        
        [JsonPropertyName("is_explored")]
        [Column("is_explored")]
        public bool IsExplored { get; set; } = false;
        
        [JsonPropertyName("terrain_rating")]
        [Column("terrain_rating")]
        public int TerrainRating { get; set; } = 1;
        
        [JsonPropertyName("resource_industry")]
        [Column("resource_industry")]
        public int ResourceIndustry { get; set; } = 0;
        
        [JsonPropertyName("resource_agriculture")]
        [Column("resource_agriculture")]
        public int ResourceAgriculture { get; set; } = 0;
        
        [JsonPropertyName("resource_building")]
        [Column("resource_building")]
        public int ResourceBuilding { get; set; } = 0;
        
        [JsonPropertyName("owner_id")]
        [Column("owner_id")]
        public string? OwnerId { get; set; }
        
        [JsonPropertyName("explored_by")]
        [Column("explored_by")]
        public List<string> ExploredBy { get; set; } = new List<string>();
    }
}
