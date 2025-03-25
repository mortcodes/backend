using HexGame.API.Models;

namespace HexGame.API.Data
{
    public interface IGameRepository
    {
        Task<Game> CreateGameAsync(int numberOfPlayers, int mapSize);
        Task<Game> GetGameAsync(string gameId);
        Task<Player> GetPlayerAsync(string playerId);
        Task<IEnumerable<Player>> GetGamePlayersAsync(string gameId);
        Task<IEnumerable<Hex>> GetGameHexesAsync(string gameId);
        Task<IEnumerable<Character>> GetGameCharactersAsync(string gameId);
        Task<IEnumerable<Card>> GetPlayerCardsAsync(string playerId);
        Task<Battle?> GetActiveBattleAsync(string gameId);

        Task UpdateGameAsync(Game game);
        Task UpdatePlayerAsync(Player player);
        Task UpdateHexAsync(Hex hex);
        Task UpdateCharacterAsync(Character character);
        Task AddCardAsync(Card card);
        Task RemoveCardAsync(string cardId);
        Task CreateBattleAsync(Battle battle);
        Task UpdateBattleAsync(Battle battle);
        Task RemoveCharacterAsync(string characterId);
    }
}
