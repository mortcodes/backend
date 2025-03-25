namespace HexGame.API.Models.DTOs
{
    public class MoveCharacterRequest
    {
        public string CharacterId { get; set; } = string.Empty;
        public int TargetQ { get; set; }
        public int TargetR { get; set; }
    }
}
