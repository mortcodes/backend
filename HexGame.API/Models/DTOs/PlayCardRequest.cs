namespace HexGame.API.Models.DTOs
{
    public class PlayCardRequest
    {
        public string CardId { get; set; } = string.Empty;
        public string? TargetCharacterId { get; set; }
        public int? TargetHexQ { get; set; }
        public int? TargetHexR { get; set; }
        public bool PlayForTurnResolution { get; set; } = true; // Default to true for the new system
    }
}
