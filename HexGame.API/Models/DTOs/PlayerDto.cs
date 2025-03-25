namespace HexGame.API.Models.DTOs
{
    public class PlayerDto
    {
        public string Id { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public int PlayerIndex { get; set; }
        public List<CardDto> Hand { get; set; } = new List<CardDto>();
        public bool IsActive { get; set; } = true;
        public List<CharacterDto> Characters { get; set; } = new List<CharacterDto>();
    }
}