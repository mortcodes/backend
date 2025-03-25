namespace HexGame.API.Models.DTOs
{
    public class CharacterDto
    {
        public string Id { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public int Q { get; set; }
        public int R { get; set; }
        public int Melee { get; set; }
        public int Magic { get; set; }
        public int Diplomacy { get; set; }
        public int MovementPoints { get; set; } = 2;
        public int MaxMovementPoints { get; set; } = 2;
    }
}