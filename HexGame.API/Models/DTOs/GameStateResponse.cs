namespace HexGame.API.Models.DTOs
{
    public class GameStateResponse
    {
        public string GameId { get; set; } = string.Empty;
        public int NumberOfPlayers { get; set; }
        public int MapSize { get; set; }
        public int CurrentTurn { get; set; }
        public int CurrentPlayerIndex { get; set; }
        public GameStatus Status { get; set; }
        public PlayerDto CurrentPlayer { get; set; } = new PlayerDto();
        public List<HexDto> ExploredHexes { get; set; } = new List<HexDto>();
        public List<CharacterDto> Characters { get; set; } = new List<CharacterDto>();
        public List<CardDto> Hand { get; set; } = new List<CardDto>();
        public BattleDto? ActiveBattle { get; set; }
        public bool IsPlayerTurn { get; set; }
        public bool HasPlayerLost { get; set; }
        public List<string> ParticipantPlayerIds { get; set; } = new List<string>();
        public List<string> SubmittedTurnPlayerIds { get; set; } = new List<string>();
    }
}
