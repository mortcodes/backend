namespace HexGame.API.Models.DTOs
{
    public class BattleUpdateRequest
    {
        public string BattleId { get; set; } = string.Empty;
        public BattleType? BattleType { get; set; }
        public bool? SubmitTurn { get; set; }
        public List<string> CardIds { get; set; } = new List<string>();
    }
}
