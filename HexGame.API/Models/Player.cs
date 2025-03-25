using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models;

namespace HexGame.API.Models
{
    [Table("players")]
    public class Player : BaseModel
    {
        [JsonPropertyName("id")]
        [Column("id")]
        [PrimaryKey("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [JsonPropertyName("game_id")]
        [Column("game_id")]
        public string GameId { get; set; } = string.Empty;
        
        [JsonPropertyName("player_index")]
        [Column("player_index")]
        public int PlayerIndex { get; set; }

        [JsonIgnore]
        [Reference(typeof(Card))]
        public List<Card> Hand { get; set; } = new List<Card>();
        
        [JsonPropertyName("is_active")]
        [Column("is_active")]
        public bool IsActive { get; set; } = true;
        
        [JsonIgnore]
        [Reference(typeof(Character))]
        public List<Character> Characters { get; set; } = new List<Character>();

    }

}
