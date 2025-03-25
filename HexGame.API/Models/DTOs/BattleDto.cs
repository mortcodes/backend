namespace HexGame.API.Models.DTOs
{
    public class BattleDto
    {
        public string Id { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string AttackerCharacterId { get; set; } = string.Empty;
        public string DefenderCharacterId { get; set; } = string.Empty;
        public int HexQ { get; set; }
        public int HexR { get; set; }
        public BattleType BattleType { get; set; } = BattleType.Melee;
        public int AttackerScore { get; set; }
        public int DefenderScore { get; set; }
        public int TerrainBonus { get; set; }
        public string? WinnerId { get; set; }
        public bool IsCompleted { get; set; } = false;
        public string? CurrentPlayerTurn { get; set; }
        public List<string> CardsPlayed { get; set; } = new List<string>();
        public List<string> PlayerPassed { get; set; } = new List<string>();
    }
}