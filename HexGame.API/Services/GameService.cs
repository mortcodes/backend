using System.Collections.Generic;
using System.Text.Json;
using HexGame.API.Data;
using HexGame.API.Models;
using HexGame.API.Models.DTOs;

namespace HexGame.API.Services
{
    public class GameService : IGameService
    {
        private readonly IGameRepository _gameRepository;

        public GameService(IGameRepository gameRepository)
        {
            _gameRepository = gameRepository;
        }

        public async Task<CreateGameResponse> CreateGameAsync(CreateGameRequest request)
        {
            // Validate input parameters
            if (request.NumberOfPlayers < 2 || request.NumberOfPlayers > 8)
            {
                throw new ArgumentException("Number of players must be between 2 and 8");
            }

            if (request.MapSize < 3 || request.MapSize > 10)
            {
                throw new ArgumentException("Map size must be between 3 and 10");
            }

            try
            {
                // Create the game
                var game = await _gameRepository.CreateGameAsync(request.NumberOfPlayers, request.MapSize);
                
                // Store all player IDs as participants
                game.ParticipantPlayerIds = game.Players.Select(p => p.Id).ToList();
                await _gameRepository.UpdateGameAsync(game);

                // Prepare response
                var response = new CreateGameResponse
                {
                    GameId = game.Id,
                    PlayerIds = game.Players.Select(p => p.Id).ToList()
                };

                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating game: {ex.Message}");
                throw;
            }
        }

        public async Task<GameStateResponse> GetGameStateAsync(string gameId, string playerId)
        {
            // Get the entire game state from the repository
            var game = await _gameRepository.GetGameAsync(gameId);
            
     
            // Get the player
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null && !game.ParticipantPlayerIds.Contains(playerId))
            {
                throw new ArgumentException($"Player with ID {playerId} not found in game {gameId}");
            }

            // Create response with only information visible to this player
            return CreateGameStateResponse(game, player);
        }

        public async Task<GameStateResponse> MoveCharacterAsync(string gameId, string playerId, MoveCharacterRequest request)
        {
            // Get the entire game state
            var game = await _gameRepository.GetGameAsync(gameId);
            
            // Validate that it's the player's turn
            var (player, isPlayerTurn) = ValidatePlayerTurn(game, playerId);

            // Only allow movement if it's the player's turn
            if (!isPlayerTurn)
            {
                throw new ArgumentException("It's not your turn. You can view the game but not make changes.");
            }

            // Get the character
            var character = player.Characters.FirstOrDefault(c => c.Id == request.CharacterId);
            if (character == null)
            {
                throw new ArgumentException($"Character with ID {request.CharacterId} not found for player {playerId}");
            }

            // Get the target hex
            var targetHex = game.Hexes.FirstOrDefault(h => h.Q == request.TargetQ && h.R == request.TargetR);
            if (targetHex == null)
            {
                throw new ArgumentException($"Invalid target hex coordinates ({request.TargetQ}, {request.TargetR})");
            }

            // Check if the hex is adjacent to the character's current position
            if (!IsAdjacent(character.Q, character.R, targetHex.Q, targetHex.R))
            {
                throw new ArgumentException("Target hex is not adjacent to the character's current position");
            }

            // Check if the character has movement points
            if (character.MovementPoints <= 0)
            {
                throw new ArgumentException("Character has no movement points left");
            }

            // Check if the target hex is explored by this player
            bool isExplored = targetHex.ExploredBy.Contains(playerId);

            // If hex is not explored, explore it (costs 1 movement point)
            if (!isExplored)
            {
                // Reduce movement point for exploration
                character.MovementPoints--;
                
                // Mark hex as explored by this player
                targetHex.IsExplored = true;
                targetHex.ExploredBy.Add(playerId);
                await _gameRepository.UpdateHexAsync(targetHex);
            }

            // Check if there's an opponent character on the target hex
            var occupyingCharacter = game.Players
                .SelectMany(p => p.Characters)
                .FirstOrDefault(c => c.Q == targetHex.Q && c.R == targetHex.R && c.PlayerId != playerId);

            // If there's an opponent on the hex, initiate battle
            if (occupyingCharacter != null)
            {
                // Create a battle
                var battle = new Battle
                {
                    GameId = gameId,
                    AttackerCharacterId = character.Id,
                    DefenderCharacterId = occupyingCharacter.Id,
                    HexQ = targetHex.Q,
                    HexR = targetHex.R,
                    TerrainBonus = targetHex.TerrainRating,
                    CurrentPlayerTurn = occupyingCharacter.PlayerId // Defender chooses battle type first
                };

                await _gameRepository.CreateBattleAsync(battle);

                // Don't move the character yet, wait for battle resolution
                // Return the game state with active battle
                return await GetGameStateAsync(gameId, playerId);
            }

            // Move character to the target hex
            character.Q = targetHex.Q;
            character.R = targetHex.R;
            character.MovementPoints -= targetHex.TerrainRating > 0 ? 1 : 0; // Moving costs 1 point (minimum)
            await _gameRepository.UpdateCharacterAsync(character);

            // Update hex ownership
            targetHex.OwnerId = playerId;
            await _gameRepository.UpdateHexAsync(targetHex);

            // Return updated game state
            return await GetGameStateAsync(gameId, playerId);
        }

        public async Task<GameStateResponse> PlayCardAsync(string gameId, string playerId, PlayCardRequest request)
        {
            // Get the entire game state
            var game = await _gameRepository.GetGameAsync(gameId);
            
            // Get player
            var (player, isPlayerTurn) = ValidatePlayerTurn(game, playerId);

            // Get the card
            var card = player.Hand.FirstOrDefault(c => c.Id == request.CardId);
            if (card == null)
            {
                throw new ArgumentException($"Card with ID {request.CardId} not found in player's hand");
            }

            // Check if there's an active battle
            var activeBattle = await _gameRepository.GetActiveBattleAsync(gameId);

            // If there's an active battle, this should be a battle card
            if (activeBattle != null)
            {
                // Check if it's the player's turn in the battle
                if (activeBattle.CurrentPlayerTurn != playerId)
                {
                    throw new ArgumentException("It's not your turn in the battle");
                }

                // Check if this is a battle card
                if (card.CardType != CardType.Battle)
                {
                    throw new ArgumentException("Only battle cards can be played during a battle");
                }

                // Apply card effect to the battle
                ApplyBattleCardEffect(activeBattle, card, playerId);
                
                // Update the battle
                await _gameRepository.UpdateBattleAsync(activeBattle);
                
                // Switch to the other player's turn
                activeBattle.CurrentPlayerTurn = activeBattle.AttackerCharacterId == playerId 
                    ? game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == activeBattle.DefenderCharacterId))?.Id
                    : game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == activeBattle.AttackerCharacterId))?.Id;
                
                await _gameRepository.UpdateBattleAsync(activeBattle);
            }
            else
            {
                // For general phase cards, validate that it's the player's turn
                if (!isPlayerTurn)
                {
                    throw new ArgumentException("It's not your turn. You can view the game but not make changes.");
                }

                // Check if this is a general phase card
                if (card.CardType != CardType.General)
                {
                    throw new ArgumentException("This card can only be played during battle");
                }

                // Apply card effect based on its type
                await ApplyGeneralCardEffect(game, player, card, request);
            }

            // Remove the card from player's hand
            await _gameRepository.RemoveCardAsync(card.Id);

            // Return updated game state
            return await GetGameStateAsync(gameId, playerId);
        }

        public async Task<GameStateResponse> EndTurnAsync(string gameId, string playerId)
        {
            // Get the entire game state
            var game = await _gameRepository.GetGameAsync(gameId);
            
            // Validate that it's the player's turn
            var (player, isPlayerTurn) = ValidatePlayerTurn(game, playerId);

            // Only allow ending turn if it's actually the player's turn
            if (!isPlayerTurn)
            {
                throw new ArgumentException("It's not your turn. You can view the game but not make changes.");
            }

            // Check if there's an active battle - can't end turn during a battle
            var activeBattle = await _gameRepository.GetActiveBattleAsync(gameId);
            if (activeBattle != null)
            {
                throw new ArgumentException("Cannot end turn during an active battle");
            }

            // Process resources from owned hexes
            // foreach (var hex in game.Hexes.Where(h => h.OwnerId == playerId))
            // {
            //     player.Resources.Industry += hex.ResourceIndustry;
            //     player.Resources.Agriculture += hex.ResourceAgriculture;
            //     player.Resources.Building += hex.ResourceBuilding;
            // }
            
            // Update player resources
            await _gameRepository.UpdatePlayerAsync(player);

            // Draw 2 cards for the player
            await DrawCardsForPlayer(game.Id, playerId, 2);

            // Reset movement points for the player's characters
            foreach (var character in player.Characters)
            {
                character.MovementPoints = character.MaxMovementPoints;
                await _gameRepository.UpdateCharacterAsync(character);
            }

            // Advance to the next player
            game.CurrentPlayerIndex = (game.CurrentPlayerIndex + 1) % game.NumberOfPlayers;
            
            // If we've cycled through all players, increment the turn counter
            if (game.CurrentPlayerIndex == 0)
            {
                game.CurrentTurn++;
            }
            
            // Update the game state
            await _gameRepository.UpdateGameAsync(game);

            // Return updated game state
            return await GetGameStateAsync(gameId, playerId);
        }

        public async Task<GameStateResponse> UpdateBattleAsync(string gameId, string playerId, BattleUpdateRequest request)
        {
            // Get the entire game state
            var game = await _gameRepository.GetGameAsync(gameId);
            
            // Get the player
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null && !game.ParticipantPlayerIds.Contains(playerId))
            {
                throw new ArgumentException($"Player with ID {playerId} not found in game {gameId}");
            }

            // Get the active battle
            var battle = await _gameRepository.GetActiveBattleAsync(gameId);
            if (battle == null || battle.Id != request.BattleId)
            {
                throw new ArgumentException($"Battle with ID {request.BattleId} not found or not active");
            }

            // Check if it's the player's turn in the battle
            if (battle.CurrentPlayerTurn != playerId)
            {
                throw new ArgumentException("It's not your turn in the battle");
            }

            // If defender is setting battle type
            if (request.BattleType.HasValue && battle.CurrentPlayerTurn == game.Players.FirstOrDefault(p => 
                p.Characters.Any(c => c.Id == battle.DefenderCharacterId))?.Id)
            {
                battle.BattleType = request.BattleType.Value;
                
                // Calculate initial scores based on character stats and battle type
                var attacker = game.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == battle.AttackerCharacterId);
                var defender = game.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == battle.DefenderCharacterId);
                
                if (attacker != null && defender != null)
                {
                    switch (battle.BattleType)
                    {
                        case BattleType.Melee:
                            battle.AttackerScore = attacker.Melee;
                            battle.DefenderScore = defender.Melee + battle.TerrainBonus;
                            break;
                        case BattleType.Magic:
                            battle.AttackerScore = attacker.Magic;
                            battle.DefenderScore = defender.Magic + battle.TerrainBonus;
                            break;
                        case BattleType.Diplomacy:
                            battle.AttackerScore = attacker.Diplomacy;
                            battle.DefenderScore = defender.Diplomacy + battle.TerrainBonus;
                            break;
                    }
                }
                
                // Attacker goes first after battle type is chosen
                battle.CurrentPlayerTurn = game.Players.FirstOrDefault(p => 
                    p.Characters.Any(c => c.Id == battle.AttackerCharacterId))?.Id;
                
                await _gameRepository.UpdateBattleAsync(battle);
            }
            // If player is passing
            else if (request.Pass.HasValue && request.Pass.Value)
            {
                // Mark player as passed
                battle.PlayerPassed.Add(playerId);
                
                // If both players have passed, resolve the battle
                if (battle.PlayerPassed.Count >= 2 || battle.PlayerPassed.Contains(
                    game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.AttackerCharacterId))?.Id) && 
                    battle.PlayerPassed.Contains(
                    game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.DefenderCharacterId))?.Id))
                {
                    await ResolveBattleAsync(game, battle);
                }
                else
                {
                    // Switch to other player
                    battle.CurrentPlayerTurn = battle.CurrentPlayerTurn == 
                        game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.AttackerCharacterId))?.Id
                        ? game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.DefenderCharacterId))?.Id
                        : game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.AttackerCharacterId))?.Id;
                        
                    await _gameRepository.UpdateBattleAsync(battle);
                }
            }
            // If player is playing a card
            else if (!string.IsNullOrEmpty(request.CardId))
            {
                // Get the card
                var card = player.Hand.FirstOrDefault(c => c.Id == request.CardId);
                if (card == null)
                {
                    throw new ArgumentException($"Card with ID {request.CardId} not found in player's hand");
                }

                // Check if it's a battle card
                if (card.CardType != CardType.Battle)
                {
                    throw new ArgumentException("Only battle cards can be played during a battle");
                }

                // Apply card effect
                ApplyBattleCardEffect(battle, card, playerId);
                
                // Add card to played cards list
                battle.CardsPlayed.Add(card.Id);
                
                // Switch to other player
                battle.CurrentPlayerTurn = battle.CurrentPlayerTurn == 
                    game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.AttackerCharacterId))?.Id
                    ? game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.DefenderCharacterId))?.Id
                    : game.Players.FirstOrDefault(p => p.Characters.Any(c => c.Id == battle.AttackerCharacterId))?.Id;
                    
                await _gameRepository.UpdateBattleAsync(battle);
                
                // Remove card from player's hand
                await _gameRepository.RemoveCardAsync(card.Id);
            }

            return await GetGameStateAsync(gameId, playerId);
        }

        // Private helper methods

        private GameStateResponse CreateGameStateResponse(Game game, Player player)
        {
            // Get all characters for this game
            var allCharacters = game.Players.SelectMany(p => p.Characters).ToList();
            
            // Get hexes that have been explored by this player
            var exploredHexes = game.Hexes.Where(h => h.ExploredBy.Contains(player.Id)).ToList();
            
            // Find unexplored but adjacent hexes (for fog of war effect)
            var visibleUnexploredHexes = new List<Hex>();
            foreach (var exploredHex in exploredHexes)
            {
                // Find all unexplored hexes adjacent to each explored hex
                var adjacentHexes = game.Hexes.Where(h => 
                    !h.ExploredBy.Contains(player.Id) && // Not explored by this player
                    IsAdjacent(exploredHex.Q, exploredHex.R, h.Q, h.R) // Adjacent to an explored hex
                ).ToList();
                
                // Add them to our visible unexplored hexes list
                visibleUnexploredHexes.AddRange(adjacentHexes);
            }
            
            // Combine explored hexes with visible unexplored ones, removing duplicates
            var allVisibleHexes = exploredHexes
                .Union(visibleUnexploredHexes, new HexCoordinateComparer())
                .ToList();
            
            // Get active battle if any
            var activeBattle = _gameRepository.GetActiveBattleAsync(game.Id).GetAwaiter().GetResult();

            // Map models to DTOs
            var playerDto = new PlayerDto
            {
                Id = player.Id,
                GameId = player.GameId,
                PlayerIndex = player.PlayerIndex,
                IsActive = player.IsActive,
                Hand = player.Hand.Select(c => new CardDto
                {
                    Id = c.Id,
                    GameId = c.GameId,
                    PlayerId = c.PlayerId,
                    CardType = c.CardType,
                    CardDefinitionId = c.CardDefinitionId,
                    Name = c.Name,
                    Description = c.Description,
                    Effect = new CardEffectDto
                    {
                        StatBonus = c.Effect.StatBonus,
                        AffectsStat = c.Effect.AffectsStat,
                        NegateTerrainBonus = c.Effect.NegateTerrainBonus,
                        AdditionalMovement = c.Effect.AdditionalMovement
                    }
                }).ToList(),
                Characters = player.Characters.Select(c => new CharacterDto
                {
                    Id = c.Id,
                    GameId = c.GameId,
                    PlayerId = c.PlayerId,
                    Q = c.Q,
                    R = c.R,
                    Melee = c.Melee,
                    Magic = c.Magic,
                    Diplomacy = c.Diplomacy,
                    MovementPoints = c.MovementPoints,
                    MaxMovementPoints = c.MaxMovementPoints
                }).ToList()
            };

            var characterDtos = allCharacters.Select(c => new CharacterDto
            {
                Id = c.Id,
                GameId = c.GameId,
                PlayerId = c.PlayerId,
                Q = c.Q,
                R = c.R,
                Melee = c.Melee,
                Magic = c.Magic,
                Diplomacy = c.Diplomacy,
                MovementPoints = c.MovementPoints,
                MaxMovementPoints = c.MaxMovementPoints
            }).ToList();

            var hexDtos = allVisibleHexes.Select(h => new HexDto
            {
                Id = h.Id,
                GameId = h.GameId,
                Q = h.Q,
                R = h.R,
                S = h.S,
                IsExplored = h.IsExplored,
                TerrainRating = h.TerrainRating,
                ResourceIndustry = h.ResourceIndustry,
                ResourceAgriculture = h.ResourceAgriculture,
                ResourceBuilding = h.ResourceBuilding,
                OwnerId = h.OwnerId,
                ExploredBy = h.ExploredBy.ToList()
            }).ToList();

            BattleDto? battleDto = null;
            if (activeBattle != null)
            {
                battleDto = new BattleDto
                {
                    Id = activeBattle.Id,
                    GameId = activeBattle.GameId,
                    AttackerCharacterId = activeBattle.AttackerCharacterId,
                    DefenderCharacterId = activeBattle.DefenderCharacterId,
                    HexQ = activeBattle.HexQ,
                    HexR = activeBattle.HexR,
                    BattleType = activeBattle.BattleType,
                    AttackerScore = activeBattle.AttackerScore,
                    DefenderScore = activeBattle.DefenderScore,
                    TerrainBonus = activeBattle.TerrainBonus,
                    WinnerId = activeBattle.WinnerId,
                    IsCompleted = activeBattle.IsCompleted,
                    CurrentPlayerTurn = activeBattle.CurrentPlayerTurn,
                    CardsPlayed = activeBattle.CardsPlayed.ToList(),
                    PlayerPassed = activeBattle.PlayerPassed.ToList()
                };
            }

            // Check if it's the player's turn
            bool isPlayerTurn = game.CurrentPlayerIndex == player.PlayerIndex;

            // Check if this player has lost (is part of the game but has no active characters)
            bool hasPlayerLost = game.ParticipantPlayerIds.Contains(player.Id) && 
                                 (!player.IsActive || player.Characters.Count == 0);

            var gsr = new GameStateResponse
            {
                GameId = game.Id,
                NumberOfPlayers = game.NumberOfPlayers,
                MapSize = game.MapSize,
                CurrentTurn = game.CurrentTurn,
                CurrentPlayerIndex = game.CurrentPlayerIndex,
                Status = game.Status,
                CurrentPlayer = playerDto,
                ExploredHexes = hexDtos,
                Characters = characterDtos,
                Hand = playerDto.Hand,
                ActiveBattle = battleDto,
                IsPlayerTurn = isPlayerTurn,
                HasPlayerLost = hasPlayerLost,
                ParticipantPlayerIds = game.ParticipantPlayerIds
            };
            JsonSerializer.Serialize(gsr, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"GameStateResponse: {JsonSerializer.Serialize(gsr, new JsonSerializerOptions { WriteIndented = true })}");
            return gsr;
        }

        private (Player player, bool isPlayerTurn) ValidatePlayerTurn(Game game, string playerId)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
            {
                throw new ArgumentException($"Player with ID {playerId} not found in game {game.Id}");
            }

            bool isPlayerTurn = game.CurrentPlayerIndex == player.PlayerIndex;
            return (player, isPlayerTurn);
        }

        private bool IsAdjacent(int q1, int r1, int q2, int r2)
        {
            // Convert to cube coordinates
            int s1 = -q1 - r1;
            int s2 = -q2 - r2;

            // Calculate distance using the cube coordinate distance formula for hexes
            // Adjacent hexes have exactly one coordinate that differs by 1
            int dq = Math.Abs(q1 - q2);
            int dr = Math.Abs(r1 - r2);
            int ds = Math.Abs(s1 - s2);
            
            return Math.Max(Math.Max(dq, dr), ds) == 1;
        }

        private void ApplyBattleCardEffect(Battle battle, Card card, string playerId)
        {
            var isAttacker = battle.AttackerCharacterId == playerId;
            
            // Apply card effect based on its type
            if (card.Effect.StatBonus != 0)
            {
                if (isAttacker)
                {
                    battle.AttackerScore += card.Effect.StatBonus;
                }
                else
                {
                    battle.DefenderScore += card.Effect.StatBonus;
                }
            }
            
            // Apply terrain negation if applicable
            if (card.Effect.NegateTerrainBonus && !isAttacker)
            {
                battle.DefenderScore -= battle.TerrainBonus;
                battle.TerrainBonus = 0;
            }
        }

        private async Task ApplyGeneralCardEffect(Game game, Player player, Card card, PlayCardRequest request)
        {
            // Apply resource boost
            // if (card.Effect.ResourceBonus != null)
            // {
            //     player.Resources.Industry += card.Effect.ResourceBonus.Industry;
            //     player.Resources.Agriculture += card.Effect.ResourceBonus.Agriculture;
            //     player.Resources.Building += card.Effect.ResourceBonus.Building;
                
            //     await _gameRepository.UpdatePlayerAsync(player);
            // }
            
            // Apply additional movement
            if (card.Effect.AdditionalMovement > 0 && !string.IsNullOrEmpty(request.TargetCharacterId))
            {
                var character = player.Characters.FirstOrDefault(c => c.Id == request.TargetCharacterId);
                if (character != null)
                {
                    character.MovementPoints += card.Effect.AdditionalMovement;
                    await _gameRepository.UpdateCharacterAsync(character);
                }
            }
            
            // Apply stat boost to character
            if (card.Effect.StatBonus > 0 && !string.IsNullOrEmpty(request.TargetCharacterId) &&
                card.Effect.AffectsStat == "All")
            {
                var character = player.Characters.FirstOrDefault(c => c.Id == request.TargetCharacterId);
                if (character != null)
                {
                    character.Melee += card.Effect.StatBonus;
                    character.Magic += card.Effect.StatBonus;
                    character.Diplomacy += card.Effect.StatBonus;
                    await _gameRepository.UpdateCharacterAsync(character);
                }
            }
        }

        private async Task ResolveBattleAsync(Game game, Battle battle)
        {
            // Determine winner based on final scores
            bool attackerWins = battle.AttackerScore > battle.DefenderScore;
            
            // Get characters involved
            var attacker = game.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == battle.AttackerCharacterId);
            var defender = game.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == battle.DefenderCharacterId);
            
            if (attacker != null && defender != null)
            {
                var attackerPlayer = game.Players.FirstOrDefault(p => p.Id == attacker.PlayerId);
                var defenderPlayer = game.Players.FirstOrDefault(p => p.Id == defender.PlayerId);
                
                // Get the hex where battle took place
                var battleHex = game.Hexes.FirstOrDefault(h => h.Q == battle.HexQ && h.R == battle.HexR);
                
                if (attackerWins)
                {
                    // Attacker moves to defender's hex
                    attacker.Q = defender.Q;
                    attacker.R = defender.R;
                    await _gameRepository.UpdateCharacterAsync(attacker);
                    
                    // Update hex ownership
                    if (battleHex != null)
                    {
                        battleHex.OwnerId = attacker.PlayerId;
                        await _gameRepository.UpdateHexAsync(battleHex);
                    }
                    
                    // Remove defender
                    await _gameRepository.RemoveCharacterAsync(defender.Id);
                    
                    // Set winner
                    battle.WinnerId = attacker.PlayerId;
                    
                    // Check if defender has any characters left
                    if (!defenderPlayer.Characters.Any(c => c.Id != defender.Id))
                    {
                        defenderPlayer.IsActive = false;
                        await _gameRepository.UpdatePlayerAsync(defenderPlayer);
                        
                        // Check if game is over (only one player left)
                        if (game.Players.Count(p => p.IsActive) <= 1)
                        {
                            game.Status = GameStatus.Finished;
                            await _gameRepository.UpdateGameAsync(game);
                        }
                    }
                }
                else
                {
                    // Defender wins, attacker is removed
                    await _gameRepository.RemoveCharacterAsync(attacker.Id);
                    
                    // Set winner
                    battle.WinnerId = defender.PlayerId;
                    
                    // Check if attacker has any characters left
                    if (!attackerPlayer.Characters.Any(c => c.Id != attacker.Id))
                    {
                        attackerPlayer.IsActive = false;
                        await _gameRepository.UpdatePlayerAsync(attackerPlayer);
                        
                        // Check if game is over (only one player left)
                        if (game.Players.Count(p => p.IsActive) <= 1)
                        {
                            game.Status = GameStatus.Finished;
                            await _gameRepository.UpdateGameAsync(game);
                        }
                    }
                }
            }
            
            // Mark battle as completed
            battle.IsCompleted = true;
            await _gameRepository.UpdateBattleAsync(battle);
        }

        private async Task DrawCardsForPlayer(string gameId, string playerId, int count)
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
            
            // Draw random cards
            for (int i = 0; i < count; i++)
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
                
                await _gameRepository.AddCardAsync(card);
            }
        }
    }

    public class HexCoordinateComparer : IEqualityComparer<Hex>
    {
        public bool Equals(Hex x, Hex y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
            return x.Q == y.Q && x.R == y.R;
        }

        public int GetHashCode(Hex obj)
        {
            return HashCode.Combine(obj.Q, obj.R);
        }
    }
}