using HexGame.API.Models;
using HexGame.API.Models.DTOs;

namespace HexGame.API.Services
{
    public interface IGameService
    {
        Task<CreateGameResponse> CreateGameAsync(CreateGameRequest request);
        Task<GameStateResponse> GetGameStateAsync(string gameId, string playerId);
        Task<GameStateResponse> MoveCharacterAsync(string gameId, string playerId, MoveCharacterRequest request);
        Task<GameStateResponse> PlayCardAsync(string gameId, string playerId, PlayCardRequest request);
        Task<GameStateResponse> EndTurnAsync(string gameId, string playerId);
        Task<GameStateResponse> UpdateBattleAsync(string gameId, string playerId, BattleUpdateRequest request);
    }
}