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
            Console.WriteLine($"Test query result: {JsonSerializer.Serialize(testQuery.Models)}");

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

        public async Task UpdateGameAsync(Game game)
        {
            // Update the game's timestamps
            game.UpdatedAt = DateTime.UtcNow;

            await _client.From<Game>()
                .Where(g => g.Id == game.Id)
                .Update(game);
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
            // Create a simple card pool
            var cardDefinitions = new List<(string Id, CardType Type, string Name, string Description, CardEffect Effect)>
            {
                ("battle_1", CardType.Battle, "Tactical Strike", "+3 to Melee in battle", new CardEffect { StatBonus = 3, AffectsStat = "Melee" }),
                ("battle_2", CardType.Battle, "Magic Missile", "+3 to Magic in battle", new CardEffect { StatBonus = 3, AffectsStat = "Magic" }),
                ("battle_3", CardType.Battle, "Persuasive Argument", "+3 to Diplomacy in battle", new CardEffect { StatBonus = 3, AffectsStat = "Diplomacy" }),
                ("battle_4", CardType.Battle, "Flanking Maneuver", "+2 to any stat in battle", new CardEffect { StatBonus = 2 }),
                ("battle_5", CardType.Battle, "Terrain Advantage", "Negate terrain bonus for opponent", new CardEffect { NegateTerrainBonus = true }),
                ("general_4", CardType.General, "Scout", "+1 Movement this turn", new CardEffect { AdditionalMovement = 1 }),
                ("general_5", CardType.General, "Training", "+1 to all stats permanently", new CardEffect { StatBonus = 1, AffectsStat = "All" })
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