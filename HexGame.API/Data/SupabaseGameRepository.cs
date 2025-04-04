using Supabase;
using Postgrest.Responses;
using HexGame.API.Models;
using HexGame.API.Services;
using System.Text.Json;

namespace HexGame.API.Data
{
    public class SupabaseGameRepository : IGameRepository
    {
        private readonly ISupabaseService _supabaseService;
        private readonly Client _client;

        public SupabaseGameRepository(ISupabaseService supabaseService)
        {
            _supabaseService = supabaseService;
            _client = supabaseService.GetClient();
        }

        public async Task<Game> CreateGameAsync(int numberOfPlayers, int mapSize)
        {
            // Create a new game
            var game = new Game
            {
                NumberOfPlayers = numberOfPlayers,
                MapSize = mapSize,
                Status = GameStatus.Created
            };
            var testQuery = await _client.From<Game>().Get();
            var testQueryResult = testQuery.Models.Select(game => new
            {
                game.Id,
                game.NumberOfPlayers,
                game.MapSize,
                game.Status
            });
            Console.WriteLine($"Test query result: {JsonSerializer.Serialize(testQueryResult)}");

            // Insert the game into Supabase
            Postgrest.Responses.ModeledResponse<Game> response;
            try
            {
                response = await _client.From<Game>().Insert(game);
                // ...
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Postgres error: {ex.Message}");
                // You might need to unwrap inner exceptions for more details
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                throw;
            }

            if (!response.Models.Any())
            {
                throw new Exception("Failed to create game in database");
            }

            var createdGame = response.Models.First();

            // Generate the hexagonal map
            await GenerateMapAsync(createdGame);

            // Create players
            await CreatePlayersAsync(createdGame);

            // Update game status to InProgress
            createdGame.Status = GameStatus.InProgress;
            await UpdateGameAsync(createdGame);

            return await GetGameAsync(createdGame.Id);
        }

        public async Task<Game> GetGameAsync(string gameId)
        {
            // Get game
            var gameResponse = await _client.From<Game>()
                .Where(g => g.Id == gameId)
                .Get();

            if (!gameResponse.Models.Any())
            {
                throw new Exception($"Game with ID {gameId} not found");
            }

            var game = gameResponse.Models.First();

            // Get players for this game
            game.Players = (await GetGamePlayersAsync(gameId)).ToList();

            // Get hexes for this game
            game.Hexes = (await GetGameHexesAsync(gameId)).ToList();

            // For each player, get their characters
            foreach (var player in game.Players)
            {
                var characters = await _client.From<Character>()
                    .Where(c => c.GameId == gameId && c.PlayerId == player.Id)
                    .Get();

                player.Characters = characters.Models.ToList();

                // Get player cards
                player.Hand = (await GetPlayerCardsAsync(player.Id)).ToList();
            }

            return game!;
        }

        public async Task<Player> GetPlayerAsync(string playerId)
        {
            var response = await _client.From<Player>()
                .Where(p => p.Id == playerId)
                .Get();

            if (!response.Models.Any())
            {
                throw new Exception($"Player with ID {playerId} not found");
            }

            var player = response.Models.First();

            // Get characters for this player
            var characters = await _client.From<Character>()
                .Where(c => c.PlayerId == playerId)
                .Get();

            player.Characters = characters.Models.ToList();

            // Get cards for this player
            player.Hand = (await GetPlayerCardsAsync(playerId)).ToList();

            return player!;
        }

        public async Task<IEnumerable<Player>> GetGamePlayersAsync(string gameId)
        {
            var response = await _client.From<Player>()
                .Where(p => p.GameId == gameId)
                .Get();

            return response.Models;
        }

        public async Task<IEnumerable<Hex>> GetGameHexesAsync(string gameId)
        {
            var response = await _client.From<Hex>()
                .Where(h => h.GameId == gameId)
                .Get();

            return response.Models;
        }

        public async Task<IEnumerable<Character>> GetGameCharactersAsync(string gameId)
        {
            var response = await _client.From<Character>()
                .Where(c => c.GameId == gameId)
                .Get();

            return response.Models;
        }

        public async Task<IEnumerable<Card>> GetPlayerCardsAsync(string playerId)
        {
            var response = await _client.From<Card>()
                .Where(c => c.PlayerId == playerId)
                .Get();

            return response.Models;
        }

        public async Task<Battle?> GetActiveBattleAsync(string gameId)
        {
            var response = await _client.From<Battle>()
                .Where(b => b.GameId == gameId && b.IsCompleted == false)
                .Get();

            return response.Models.FirstOrDefault();
        }

        public async Task<IEnumerable<Battle>> GetAllActiveBattlesAsync(string gameId)
        {
            var response = await _client.From<Battle>()
                .Where(b => b.GameId == gameId && b.IsCompleted == false)
                .Get();

            return response.Models;
        }

        public async Task<IEnumerable<Battle>> GetSubmittedBattlesAsync(string gameId)
        {
            try
            {
                Console.WriteLine($"Fetching submitted battles for game {gameId}");
                
                // Build the query more carefully, avoiding complex nested conditions
                // First get battles for this game that aren't completed
                var query = _client.From<Battle>()
                    .Filter("game_id", Postgrest.Constants.Operator.Equals, gameId)
                    .Filter("is_completed", Postgrest.Constants.Operator.Equals, "false");
                
                // Then filter for those where both attacker and defender have submitted
                query = query.Filter("attacker_submitted", Postgrest.Constants.Operator.Equals, true);
                query = query.Filter("defender_submitted", Postgrest.Constants.Operator.Equals, true);
                
                var response = await query.Get();
                
                Console.WriteLine($"Found {response.Models.Count()} submitted battles");
                return response.Models;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetSubmittedBattlesAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                // Return empty collection instead of throwing an exception
                return new List<Battle>();
            }
        }
        
        public async Task<IEnumerable<Battle>> GetPreviousTurnBattlesAsync(string gameId, int currentTurn)
        {
            try
            {
                Console.WriteLine($"Fetching previous turn battles for game {gameId}, current turn: {currentTurn}");
                
                // Get completed battles from the previous turn (currentTurn - 1)
                int previousTurn = currentTurn - 1;
                if (previousTurn < 1) return new List<Battle>();
                
                var query = _client.From<Battle>()
                    .Filter("game_id", Postgrest.Constants.Operator.Equals, gameId)
                    .Filter("is_completed", Postgrest.Constants.Operator.Equals, "true")
                    .Filter("completed_turn", Postgrest.Constants.Operator.Equals, previousTurn.ToString());
                
                var response = await query.Get();
                
                Console.WriteLine($"Found {response.Models.Count()} battles from previous turn");
                return response.Models;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPreviousTurnBattlesAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                // Return empty collection instead of throwing an exception
                return new List<Battle>();
            }
        }

        public async Task<IEnumerable<Card>> GetPendingCardsForPlayerAsync(string playerId)
        {
            var response = await _client.From<Card>()
                .Where(c => c.PlayerId == playerId && c.PendingResolution == true)
                .Get();

            return response.Models;
        }

        public async Task<Card?> GetCardAsync(string cardId)
        {
            var response = await _client.From<Card>()
                .Where(c => c.Id == cardId)
                .Get();

            return response.Models.FirstOrDefault();
        }

        public async Task UpdateCardAsync(Card card)
        {
            await _client.From<Card>()
                .Where(c => c.Id == card.Id)
                .Update(card);
        }

        public async Task UpdateGameAsync(Game game)
        {
            try
            {
                // Update the game's timestamps
                game.UpdatedAt = DateTime.UtcNow;
                
                // Log for debugging
                Console.WriteLine($"Updating game {game.Id}");
                Console.WriteLine($"Current turn: {game.CurrentTurn}");
                Console.WriteLine($"Players who submitted turns: {string.Join(", ", game.SubmittedTurnPlayerIds)}");
                Console.WriteLine($"Total player count: {game.Players.Count}");
                
                // Make sure the update is atomic
                await _client.From<Game>()
                    .Where(g => g.Id == game.Id)
                    .Update(game);
                
                Console.WriteLine($"Game {game.Id} updated successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating game {game.Id}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        public async Task UpdatePlayerAsync(Player player)
        {
            await _client.From<Player>()
                .Where(p => p.Id == player.Id)
                .Update(player);
        }

        public async Task UpdateHexAsync(Hex hex)
        {
            await _client.From<Hex>()
                .Where(h => h.Id == hex.Id)
                .Update(hex);
        }

        public async Task UpdateCharacterAsync(Character character)
        {
            await _client.From<Character>()
                .Where(c => c.Id == character.Id)
                .Update(character);
        }

        public async Task AddCardAsync(Card card)
        {
            await _client.From<Card>().Insert(card);
        }

        public async Task RemoveCardAsync(string cardId)
        {
            await _client.From<Card>()
                .Where(c => c.Id == cardId)
                .Delete();
        }

        public async Task CreateBattleAsync(Battle battle)
        {
            await _client.From<Battle>().Insert(battle);
        }

        public async Task UpdateBattleAsync(Battle battle)
        {
            await _client.From<Battle>()
                .Where(b => b.Id == battle.Id)
                .Update(battle);
        }

        public async Task RemoveCharacterAsync(string characterId)
        {
            await _client.From<Character>()
                .Where(c => c.Id == characterId)
                .Delete();
        }

        // Private helper methods for game creation
        private async Task GenerateMapAsync(Game game)
        {
            var hexes = new List<Hex>();
            int mapSize = game.MapSize;

            // Generate hexagonal map with cube coordinates
            for (int q = -mapSize; q <= mapSize; q++)
            {
                int r1 = Math.Max(-mapSize, -q - mapSize);
                int r2 = Math.Min(mapSize, -q + mapSize);

                for (int r = r1; r <= r2; r++)
                {
                    var s = -q - r; // In cube coordinates, q + r + s = 0

                    var hex = new Hex
                    {
                        GameId = game.Id,
                        Q = q,
                        R = r,
                        S = s,
                        TerrainRating = Random.Shared.Next(1, 6), // 1-5
                        ResourceIndustry = Random.Shared.Next(0, 6), // 0-5
                        ResourceAgriculture = Random.Shared.Next(0, 6), // 0-5
                        ResourceBuilding = Random.Shared.Next(0, 6) // 0-5
                    };

                    hexes.Add(hex);
                }
            }

            // Insert all hexes into the database
            foreach (var hex in hexes)
            {
                await _client.From<Hex>().Insert(hex);
            }
        }

        private async Task CreatePlayersAsync(Game game)
        {
            // Create players and assign them starting positions
            var hexes = (await GetGameHexesAsync(game.Id)).ToList();
            var startingHexes = AssignStartingHexes(hexes, game.NumberOfPlayers);

            for (int i = 0; i < game.NumberOfPlayers; i++)
            {
                var player = new Player
                {
                    GameId = game.Id,
                    PlayerIndex = i
                };

                // Insert player
                var playerResponse = await _client.From<Player>().Insert(player);
                var createdPlayer = playerResponse.Models.First();

                // Assign starting hex
                var startingHex = startingHexes[i];
                startingHex.OwnerId = createdPlayer.Id;
                startingHex.IsExplored = true;
                startingHex.ExploredBy.Add(createdPlayer.Id);
                await UpdateHexAsync(startingHex);

                // Create a character for the player
                var character = new Character
                {
                    GameId = game.Id,
                    PlayerId = createdPlayer.Id,
                    Q = startingHex.Q,
                    R = startingHex.R,
                    Melee = Random.Shared.Next(1, 11), // 1-10
                    Magic = Random.Shared.Next(1, 11), // 1-10
                    Diplomacy = Random.Shared.Next(1, 11) // 1-10
                };

                await _client.From<Character>().Insert(character);

                // Deal initial cards to player
                await DealInitialCardsAsync(game.Id, createdPlayer.Id);
            }
        }

        private List<Hex> AssignStartingHexes(List<Hex> hexes, int numberOfPlayers)
        {
            // Filter hexes to those within a certain distance from center
            // This ensures players start in reasonable positions, not at extreme edges
            var validStartingHexes = hexes.Where(h =>
                Math.Max(Math.Abs(h.Q), Math.Max(Math.Abs(h.R), Math.Abs(h.S))) <=
                Math.Max(2, hexes.Max(x => Math.Max(Math.Abs(x.Q), Math.Max(Math.Abs(x.R), Math.Abs(x.S)))) / 2)
            ).ToList();

            var startingHexes = new List<Hex>();
            var random = new Random();

            // Try to place starting positions at least 3 hexes apart
            for (int i = 0; i < numberOfPlayers; i++)
            {
                if (validStartingHexes.Count == 0)
                {
                    // If we run out of valid hexes, use any hex
                    var randomHex = hexes[random.Next(hexes.Count)];
                    startingHexes.Add(randomHex);
                    hexes.Remove(randomHex);
                    continue;
                }

                var candidateHex = validStartingHexes[random.Next(validStartingHexes.Count)];
                startingHexes.Add(candidateHex);

                // Remove chosen hex and nearby hexes from consideration
                validStartingHexes.RemoveAll(h =>
                    HexDistance(h, candidateHex) < 3
                );
            }

            return startingHexes;
        }

        private int HexDistance(Hex a, Hex b)
        {
            return (Math.Abs(a.Q - b.Q) + Math.Abs(a.R - b.R) + Math.Abs(a.S - b.S)) / 2;
        }

        private async Task DealInitialCardsAsync(string gameId, string playerId)
        {
            // Updated card pool with effects suitable for end-of-turn resolution
            var cardDefinitions = new List<(string Id, CardType Type, string Name, string Description, CardEffect Effect)>
            {
                // Battle cards
                ("battle_1", CardType.Battle, "Tactical Strike", "+3 to battle score in melee combat", 
                    new CardEffect { BattleBonus = 3, AffectsStat = "Melee" }),
                ("battle_2", CardType.Battle, "Magic Missile", "+3 to battle score in magic combat", 
                    new CardEffect { BattleBonus = 3, AffectsStat = "Magic" }),
                ("battle_3", CardType.Battle, "Persuasive Argument", "+3 to battle score in diplomacy", 
                    new CardEffect { BattleBonus = 3, AffectsStat = "Diplomacy" }),
                ("battle_4", CardType.Battle, "Defensive Position", "+2 to defense when defending", 
                    new CardEffect { DefensiveBonus = 2 }),
                ("battle_5", CardType.Battle, "Terrain Analysis", "Negate terrain bonus for opponent", 
                    new CardEffect { NegateTerrainBonus = true }),
                
                // General cards
                ("general_1", CardType.General, "Strategic Movement", "+1 Movement next turn", 
                    new CardEffect { AdditionalMovement = 1, EffectDuration = 1 }),
                ("general_2", CardType.General, "Training Regiment", "+1 to all stats permanently", 
                    new CardEffect { StatBonus = 1, AffectsStat = "All", EffectDuration = 1 }),
                ("general_3", CardType.General, "Combat Training", "+2 to Melee stat permanently", 
                    new CardEffect { StatBonus = 2, AffectsStat = "Melee", EffectDuration = 1 }),
                ("general_4", CardType.General, "Magic Studies", "+2 to Magic stat permanently", 
                    new CardEffect { StatBonus = 2, AffectsStat = "Magic", EffectDuration = 1 }),
                ("general_5", CardType.General, "Diplomatic Mission", "+2 to Diplomacy stat permanently", 
                    new CardEffect { StatBonus = 2, AffectsStat = "Diplomacy", EffectDuration = 1 }),
            };

            // Deal 4 initial cards
            for (int i = 0; i < 4; i++)
            {
                var randomCard = cardDefinitions[Random.Shared.Next(cardDefinitions.Count)];

                var card = new Card
                {
                    GameId = gameId,
                    PlayerId = playerId,
                    CardType = randomCard.Type,
                    CardDefinitionId = randomCard.Id,
                    Name = randomCard.Name,
                    Description = randomCard.Description,
                    Effect = randomCard.Effect
                };

                await AddCardAsync(card);
            }
        }
    }
}