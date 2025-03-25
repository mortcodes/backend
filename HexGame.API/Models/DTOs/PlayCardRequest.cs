namespace HexGame.API.Models.DTOs
{
    public class PlayCardRequest
    {
        public string CardId { get; set; } = string.Empty;
        public string? TargetCharacterId { get; set; }
        public int? TargetHexQ { get; set; }
        public int? TargetHexR { get; set; }
    }
}
