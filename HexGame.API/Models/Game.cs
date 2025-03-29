using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models;

namespace HexGame.API.Models
{
    [Table("games")]
    public class Game : BaseModel
    {
        [JsonPropertyName("id")]
        [Column("id")]
        [PrimaryKey("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [JsonPropertyName("number_of_players")]
        [Column("number_of_players")]
        public int NumberOfPlayers { get; set; }
        
        [JsonPropertyName("map_size")]
        [Column("map_size")]
        public int MapSize { get; set; }
        
        [JsonPropertyName("current_turn")]
        [Column("current_turn")]
        public int CurrentTurn { get; set; } = 1;  // Start at turn 1 instead of 0
        
        [JsonPropertyName("current_player_index")]
        [Column("current_player_index")]
        public int CurrentPlayerIndex { get; set; } = 0;
        
        [JsonPropertyName("created_at")]
        [Column("created_at")] 
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [JsonPropertyName("updated_at")]
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [JsonPropertyName("status")]
        [Column("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GameStatus Status { get; set; } = GameStatus.Created;

        [JsonPropertyName("participant_player_ids")]
        [Column("participant_player_ids")]
        public List<string> ParticipantPlayerIds { get; set; } = new List<string>();

        [JsonPropertyName("submitted_turn_player_ids")]
        [Column("submitted_turn_player_ids")]
        public List<string> SubmittedTurnPlayerIds { get; set; } = new List<string>();

        // Not stored directly in the database, populated when needed
        [JsonIgnore]
        [Reference(typeof(Player))]
        public List<Player> Players { get; set; } = new List<Player>();
        
        [JsonIgnore]
        [Reference(typeof(Hex))]
        public List<Hex> Hexes { get; set; } = new List<Hex>();
    }

    public enum GameStatus
    {
        Created,
        InProgress,
        Finished
    }
}
