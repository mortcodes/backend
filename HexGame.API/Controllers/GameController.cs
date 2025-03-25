using Microsoft.AspNetCore.Mvc;
using HexGame.API.Models.DTOs;
using HexGame.API.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexGame.API.Controllers
{
    [ApiController]
    [Route("api/game")]
    public class GameController : ControllerBase
    {
        private readonly IGameService _gameService;
        private readonly JsonSerializerOptions _jsonOptions;

        public GameController(IGameService gameService)
        {
            _gameService = gameService;
            _jsonOptions = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,  // Changed from Preserve to IgnoreCycles
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                MaxDepth = 64
            };
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        [HttpPost("create")]
        public async Task<ActionResult<CreateGameResponse>> CreateGame([FromBody] CreateGameRequest request)
        {
            try
            {
                Console.WriteLine("Received request to create game");
                var response = await _gameService.CreateGameAsync(request);
                Console.WriteLine("Game created successfully");
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"ArgumentException: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                // Log inner exception details if available
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, new { error = "An error occurred while creating the game", details = ex.Message });
            }
        }

        [HttpGet("{gameId}/{playerId}")]
        public async Task<ActionResult<GameStateResponse>> GetGameState(string gameId, string playerId)
        {
            try
            {
                var response = await _gameService.GetGameStateAsync(gameId, playerId);
                return new JsonResult(response, _jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred while retrieving the game state", details = ex.Message });
            }
        }

        [HttpPost("{gameId}/{playerId}/move")]
        public async Task<ActionResult<GameStateResponse>> MoveCharacter(string gameId, string playerId, [FromBody] MoveCharacterRequest request)
        {
            try
            {
                var response = await _gameService.MoveCharacterAsync(gameId, playerId, request);
                return new JsonResult(response, _jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred while moving the character", details = ex.Message });
            }
        }

        [HttpPost("{gameId}/{playerId}/play-card")]
        public async Task<ActionResult<GameStateResponse>> PlayCard(string gameId, string playerId, [FromBody] PlayCardRequest request)
        {
            try
            {
                var response = await _gameService.PlayCardAsync(gameId, playerId, request);
                return new JsonResult(response, _jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred while playing the card", details = ex.Message });
            }
        }

        [HttpPost("{gameId}/{playerId}/end-turn")]
        public async Task<ActionResult<GameStateResponse>> EndTurn(string gameId, string playerId)
        {
            try
            {
                var response = await _gameService.EndTurnAsync(gameId, playerId);
                return new JsonResult(response, _jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred while ending the turn", details = ex.Message });
            }
        }

        [HttpPost("{gameId}/{playerId}/battle")]
        public async Task<ActionResult<GameStateResponse>> UpdateBattle(string gameId, string playerId, [FromBody] BattleUpdateRequest request)
        {
            try
            {
                var response = await _gameService.UpdateBattleAsync(gameId, playerId, request);
                return new JsonResult(response, _jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred while updating the battle", details = ex.Message });
            }
        }

        [HttpGet("test")]
        public ActionResult<string> TestApi()
        {
            return Ok("API is working");
        }
    }
}