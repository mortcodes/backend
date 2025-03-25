namespace HexGame.API.Models.DTOs
{
    public class BattleUpdateRequest
    {
        public string BattleId { get; set; } = string.Empty;
        public BattleType? BattleType { get; set; }
        public bool? Pass { get; set; }
        public string? CardId { get; set; }
    }
}
