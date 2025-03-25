namespace HexGame.API.Models.DTOs
{
    public class CreateGameResponse
    {
        public string GameId { get; set; } = string.Empty;
        public List<string> PlayerIds { get; set; } = new List<string>();
    }
}
