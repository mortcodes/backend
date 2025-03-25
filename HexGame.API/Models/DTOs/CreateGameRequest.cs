namespace HexGame.API.Models.DTOs
{
    public class CreateGameRequest
    {
        public int NumberOfPlayers { get; set; }
        public int MapSize { get; set; }
    }
}
