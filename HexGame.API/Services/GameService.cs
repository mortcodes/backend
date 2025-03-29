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

            // If the player is null but is in participant IDs, they likely lost the game
            // Create a placeholder player to avoid null reference exceptions
            if (player == null && game.ParticipantPlayerIds.Contains(playerId))
            {
                player = new Player 
                { 
                    Id = playerId,
                    GameId = gameId,
                    IsActive = false,
                    Characters = new List<Character>(),
                    Hand = new List<Card>()
                };
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

            // Only allow playing cards during your turn
            if (!isPlayerTurn)
            {
                throw new ArgumentException("You can only play cards during your turn.");
            }

            // Get the card
            var card = player.Hand.FirstOrDefault(c => c.Id == request.CardId);
            if (card == null)
            {
                throw new ArgumentException($"Card with ID {request.CardId} not found in player's hand");
            }

            // Check if there's an active battle
            var activeBattle = await _gameRepository.GetActiveBattleAsync(gameId);

            // You can't play cards during battles anymore, must use battle update request
            if (activeBattle != null)
            {
                throw new ArgumentException("You cannot play cards directly during battle. Use the battle update to submit your battle actions.");
            }

            // Queue the card for end-of-turn resolution
            card.PendingResolution = true;
            card.PlayedOnTurn = game.CurrentTurn;
            
            // Store target information
            if (!string.IsNullOrEmpty(request.TargetCharacterId))
            {
                card.TargetId = request.TargetCharacterId;
                
                // Verify target character exists
                var targetCharacter = game.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == request.TargetCharacterId);
                if (targetCharacter == null)
                {
                    throw new ArgumentException($"Target character with ID {request.TargetCharacterId} not found");
                }
            }
            
            // Update the card
            await _gameRepository.UpdateCardAsync(card);

            // Remove the card from player's hand
            player.Hand.Remove(card);
            await _gameRepository.UpdatePlayerAsync(player);

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
            
            try
            {
                // Create a working copy of the game - don't modify the original yet
                var workingCopy = await _gameRepository.GetGameAsync(gameId);
                var workingPlayer = workingCopy.Players.First(p => p.Id == playerId);
            
                // Process resources from owned hexes (commented out as it's not implemented in original code)
                // foreach (var hex in workingCopy.Hexes.Where(h => h.OwnerId == playerId))
                // {
                //     workingPlayer.Resources.Industry += hex.ResourceIndustry;
                //     workingPlayer.Resources.Agriculture += hex.ResourceAgriculture;
                //     workingPlayer.Resources.Building += hex.ResourceBuilding;
                // }

                // Resolve pending card effects without persisting them yet
                var pendingCards = await _gameRepository.GetPendingCardsForPlayerAsync(playerId);
                var updatedCharacters = new List<Character>();
                var cardsToRemove = new List<string>();
                var cardsToUpdate = new List<Card>();
                
                foreach (var card in pendingCards)
                {
                    // Apply card effects based on their type without persisting the changes
                    switch (card.CardType)
                    {
                        case CardType.General:
                            // Apply general card effects
                            if (card.Effect.AdditionalMovement > 0 && !string.IsNullOrEmpty(card.TargetId))
                            {
                                // Find the target character
                                var targetCharacter = workingPlayer.Characters.FirstOrDefault(c => c.Id == card.TargetId);
                                if (targetCharacter != null)
                                {
                                    targetCharacter.MovementPoints += card.Effect.AdditionalMovement;
                                    updatedCharacters.Add(targetCharacter);
                                }
                            }
                            
                            // Apply permanent stat boosts
                            if (card.Effect.StatBonus > 0 && !string.IsNullOrEmpty(card.TargetId))
                            {
                                var targetCharacter = workingPlayer.Characters.FirstOrDefault(c => c.Id == card.TargetId);
                                if (targetCharacter != null)
                                {
                                    if (card.Effect.AffectsStat == "All" || card.Effect.AffectsStat == "Melee")
                                    {
                                        targetCharacter.Melee += card.Effect.StatBonus;
                                    }
                                    if (card.Effect.AffectsStat == "All" || card.Effect.AffectsStat == "Magic")
                                    {
                                        targetCharacter.Magic += card.Effect.StatBonus;
                                    }
                                    if (card.Effect.AffectsStat == "All" || card.Effect.AffectsStat == "Diplomacy")
                                    {
                                        targetCharacter.Diplomacy += card.Effect.StatBonus;
                                    }
                                    
                                    updatedCharacters.Add(targetCharacter);
                                }
                            }
                            break;
                            
                        case CardType.Strategy:
                            // Strategy cards would be applied here
                            break;
                    }
                    
                    // Track cards to be removed or updated
                    if (card.Effect.EffectDuration <= 1)
                    {
                        // One-time effect, remove card
                        cardsToRemove.Add(card.Id);
                    }
                    else
                    {
                        // Multi-turn effect, decrement duration and keep card
                        card.Effect.EffectDuration--;
                        card.PendingResolution = false;
                        cardsToUpdate.Add(card);
                    }
                }
                
                // Prepare character updates without persisting yet
                foreach (var character in workingPlayer.Characters)
                {
                    character.MovementPoints = character.MaxMovementPoints;
                    if (!updatedCharacters.Any(c => c.Id == character.Id))
                    {
                        updatedCharacters.Add(character);
                    }
                }
                
                // Mark this player as having submitted their turn (on working copy only)
                if (!workingCopy.SubmittedTurnPlayerIds.Contains(playerId))
                {
                    workingCopy.SubmittedTurnPlayerIds.Add(playerId);
                }
                
                // Check if all active players have submitted their turns in the working copy
                bool shouldResolveTurn = true;
                foreach (var activePlayer in workingCopy.Players.Where(p => p.IsActive))
                {
                    if (!workingCopy.SubmittedTurnPlayerIds.Contains(activePlayer.Id))
                    {
                        shouldResolveTurn = false;
                        break;
                    }
                }
                
                // If all players have submitted, apply turn resolution on working copy
                if (shouldResolveTurn)
                {
                    // Resolve all battles in the working copy
                    var battles = await _gameRepository.GetSubmittedBattlesAsync(gameId);
                    foreach (var battle in battles)
                    {
                        if (battle.AttackerSubmitted && battle.DefenderSubmitted && !battle.IsCompleted)
                        {
                            // Process battle cards and update scores in memory
                            // (Implementation depends on ResolveSubmittedBattlesAsync and ResolveBattleAsync)
                            // This would be complex to duplicate here, so we'll just issue a warning
                            Console.WriteLine("Warning: Simulating battle resolution in memory is not implemented.");
                        }
                    }
                    
                    // Prepare turn advancement in memory
                    workingCopy.CurrentTurn++;
                    workingCopy.SubmittedTurnPlayerIds.Clear();
                    workingCopy.CurrentPlayerIndex = 0;
                }
                
                // All operations successful in memory, now persist the changes to the database
                
                // 1. Update player state
                await _gameRepository.UpdatePlayerAsync(player);
                
                // 2. Apply card effects
                foreach (var character in updatedCharacters)
                {
                    await _gameRepository.UpdateCharacterAsync(character);
                }
                
                // 3. Update/remove cards
                foreach (var cardId in cardsToRemove)
                {
                    await _gameRepository.RemoveCardAsync(cardId);
                }
                
                foreach (var card in cardsToUpdate)
                {
                    await _gameRepository.UpdateCardAsync(card);
                }
                
                // 4. Draw new cards
                await DrawCardsForPlayer(gameId, playerId, 2);
                
                // 5. Update submitted turn status
                if (!game.SubmittedTurnPlayerIds.Contains(playerId))
                {
                    game.SubmittedTurnPlayerIds.Add(playerId);
                    await _gameRepository.UpdateGameAsync(game);
                }
                
                // 6. If all players have submitted, resolve the turn
                if (shouldResolveTurn)
                {
                    await ResolveSubmittedBattlesAsync(game);
                    
                    // Update game state after battle resolution
                    game.CurrentTurn = workingCopy.CurrentTurn;
                    game.SubmittedTurnPlayerIds.Clear();
                    game.CurrentPlayerIndex = 0;
                    await _gameRepository.UpdateGameAsync(game);
                }
                
                // Return updated game state
                return await GetGameStateAsync(gameId, playerId);
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error in EndTurnAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                // Rethrow so the controller can handle it
                throw;
            }
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

            // If the player is null but is in participant IDs, they likely lost the game
            if (player == null && game.ParticipantPlayerIds.Contains(playerId))
            {
                throw new ArgumentException("You have been eliminated from the game and cannot make changes.");
            }

            // Get the active battle
            var battle = await _gameRepository.GetActiveBattleAsync(gameId);
            if (battle == null || battle.Id != request.BattleId)
            {
                throw new ArgumentException($"Battle with ID {request.BattleId} not found or not active");
            }

            // Check if this player is involved in the battle
            var isAttacker = IsAttackerPlayer(game, battle, playerId);
            var isDefender = IsDefenderPlayer(game, battle, playerId);
            
            if (!isAttacker && !isDefender)
            {
                throw new ArgumentException("You are not involved in this battle");
            }

            // Check if player already submitted their turn
            if ((isAttacker && battle.AttackerSubmitted) || (isDefender && battle.DefenderSubmitted))
            {
                throw new ArgumentException("You have already submitted your actions for this battle");
            }

            try
            {
                // Create working copies - don't modify the originals yet
                var workingBattle = new Battle
                {
                    Id = battle.Id,
                    GameId = battle.GameId,
                    AttackerCharacterId = battle.AttackerCharacterId,
                    DefenderCharacterId = battle.DefenderCharacterId,
                    HexQ = battle.HexQ,
                    HexR = battle.HexR,
                    BattleType = battle.BattleType,
                    AttackerScore = battle.AttackerScore,
                    DefenderScore = battle.DefenderScore,
                    TerrainBonus = battle.TerrainBonus,
                    AttackerSubmitted = battle.AttackerSubmitted,
                    DefenderSubmitted = battle.DefenderSubmitted,
                    WinnerId = battle.WinnerId,
                    IsCompleted = battle.IsCompleted,
                    CurrentPlayerTurn = battle.CurrentPlayerTurn,
                    AttackerCardsPlayed = new List<string>(battle.AttackerCardsPlayed),
                    DefenderCardsPlayed = new List<string>(battle.DefenderCardsPlayed)
                };
                
                var workingGame = await _gameRepository.GetGameAsync(gameId);
                var cardsToProcess = new List<Card>();
                
                // If defender is choosing battle type
                if (request.BattleType.HasValue && isDefender && string.IsNullOrEmpty(workingBattle.CurrentPlayerTurn))
                {
                    workingBattle.BattleType = request.BattleType.Value;
                    
                    // Calculate initial scores based on character stats and battle type
                    var attacker = workingGame.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == workingBattle.AttackerCharacterId);
                    var defender = workingGame.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == workingBattle.DefenderCharacterId);
                    
                    if (attacker != null && defender != null)
                    {
                        switch (workingBattle.BattleType)
                        {
                            case BattleType.Melee:
                                workingBattle.AttackerScore = attacker.Melee;
                                workingBattle.DefenderScore = defender.Melee + workingBattle.TerrainBonus;
                                break;
                            case BattleType.Magic:
                                workingBattle.AttackerScore = attacker.Magic;
                                workingBattle.DefenderScore = defender.Magic + workingBattle.TerrainBonus;
                                break;
                            case BattleType.Diplomacy:
                                workingBattle.AttackerScore = attacker.Diplomacy;
                                workingBattle.DefenderScore = defender.Diplomacy + workingBattle.TerrainBonus;
                                break;
                        }
                    }
                }

                // Process card submissions in memory first
                if (request.CardIds != null && request.CardIds.Count > 0)
                {
                    foreach (var cardId in request.CardIds)
                    {
                        // Verify card exists and is in player's hand
                        var card = player.Hand.FirstOrDefault(c => c.Id == cardId);
                        if (card == null)
                        {
                            throw new ArgumentException($"Card with ID {cardId} not found in player's hand");
                        }

                        // Check if it's a battle card
                        if (card.CardType != CardType.Battle)
                        {
                            throw new ArgumentException("Only battle cards can be played during a battle");
                        }

                        // Mark the card as pending resolution
                        var workingCard = new Card
                        {
                            Id = card.Id,
                            GameId = card.GameId,
                            PlayerId = card.PlayerId,
                            CardType = card.CardType,
                            CardDefinitionId = card.CardDefinitionId,
                            Name = card.Name,
                            Description = card.Description,
                            Effect = card.Effect,
                            PendingResolution = true,
                            PlayedOnTurn = workingGame.CurrentTurn,
                            TargetId = card.TargetId
                        };
                        
                        cardsToProcess.Add(workingCard);
                        
                        // Store the card ID in the appropriate list
                        if (isAttacker)
                        {
                            workingBattle.AttackerCardsPlayed.Add(cardId);
                        }
                        else
                        {
                            workingBattle.DefenderCardsPlayed.Add(cardId);
                        }
                    }
                }

                // Track turn submission in memory
                bool markPlayerAsSubmitted = false;
                bool shouldResolveTurn = false;
                
                // Mark player as submitted if they're submitting their turn
                if (request.SubmitTurn.HasValue && request.SubmitTurn.Value)
                {
                    if (isAttacker)
                    {
                        workingBattle.AttackerSubmitted = true;
                        
                        // Also mark this player as having submitted their turn for the round
                        if (!workingGame.SubmittedTurnPlayerIds.Contains(playerId))
                        {
                            workingGame.SubmittedTurnPlayerIds.Add(playerId);
                            markPlayerAsSubmitted = true;
                        }
                    }
                    else
                    {
                        workingBattle.DefenderSubmitted = true;
                        
                        // Also mark this player as having submitted their turn for the round
                        if (!workingGame.SubmittedTurnPlayerIds.Contains(playerId))
                        {
                            workingGame.SubmittedTurnPlayerIds.Add(playerId);
                            markPlayerAsSubmitted = true;
                        }
                    }
                    
                    // Check if all players have now submitted their turns
                    shouldResolveTurn = true;
                    foreach (var activePlayer in workingGame.Players.Where(p => p.IsActive))
                    {
                        if (!workingGame.SubmittedTurnPlayerIds.Contains(activePlayer.Id))
                        {
                            shouldResolveTurn = false;
                            break;
                        }
                    }
                }
                
                // Now that we've validated everything and prepared the changes in memory, 
                // apply the changes to the database
                
                // 1. Update battle type and scores if needed
                if (request.BattleType.HasValue && isDefender && string.IsNullOrEmpty(battle.CurrentPlayerTurn))
                {
                    battle.BattleType = workingBattle.BattleType;
                    battle.AttackerScore = workingBattle.AttackerScore;
                    battle.DefenderScore = workingBattle.DefenderScore;
                }

                // 2. Process cards
                foreach (var workingCard in cardsToProcess)
                {
                    var card = player.Hand.FirstOrDefault(c => c.Id == workingCard.Id);
                    if (card != null)
                    {
                        card.PendingResolution = true;
                        card.PlayedOnTurn = game.CurrentTurn;
                        await _gameRepository.UpdateCardAsync(card);
                        
                        // Add card ID to the battle
                        if (isAttacker && !battle.AttackerCardsPlayed.Contains(workingCard.Id))
                        {
                            battle.AttackerCardsPlayed.Add(workingCard.Id);
                        }
                        else if (isDefender && !battle.DefenderCardsPlayed.Contains(workingCard.Id))
                        {
                            battle.DefenderCardsPlayed.Add(workingCard.Id);
                        }
                    }
                }

                // 3. Update submission state
                if (request.SubmitTurn.HasValue && request.SubmitTurn.Value)
                {
                    if (isAttacker)
                    {
                        battle.AttackerSubmitted = true;
                    }
                    else
                    {
                        battle.DefenderSubmitted = true;
                    }
                    
                    // Mark player as having submitted their turn in the real game object
                    if (markPlayerAsSubmitted && !game.SubmittedTurnPlayerIds.Contains(playerId))
                    {
                        game.SubmittedTurnPlayerIds.Add(playerId);
                        await _gameRepository.UpdateGameAsync(game);
                    }
                }
                
                // 4. If both players have submitted, mark battle for resolution
                if (workingBattle.AttackerSubmitted && workingBattle.DefenderSubmitted)
                {
                    // Battle will be resolved during end turn
                    battle.CurrentPlayerTurn = null; // No player's turn since both submitted
                }
                
                // 5. Update the battle with all changes
                await _gameRepository.UpdateBattleAsync(battle);
                
                // 6. If all players have submitted their turns, resolve the end of turn
                if (shouldResolveTurn)
                {
                    // Only attempt to resolve the turn if all validations and updates have succeeded
                    var turnResolved = await ResolveEndOfTurn(game);
                    if (!turnResolved)
                    {
                        Console.WriteLine("Turn resolution was expected but not performed");
                    }
                }
                
                return await GetGameStateAsync(gameId, playerId);
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error in UpdateBattleAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                
                // Rethrow so the controller can handle it
                throw;
            }
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
                    PlayerPassed = activeBattle.PlayerPassed.ToList(),
                    BattleHistory = activeBattle.BattleHistory
                };
            }

            // Check if it's the player's turn
            bool hasSubmittedTurn = game.SubmittedTurnPlayerIds.Contains(player.Id);
            // Player can take a turn if they are active and haven't submitted their turn yet
            bool isPlayerTurn = player.IsActive && !hasSubmittedTurn;

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
                ParticipantPlayerIds = game.ParticipantPlayerIds,
                SubmittedTurnPlayerIds = game.SubmittedTurnPlayerIds
            };
            JsonSerializer.Serialize(gsr, new JsonSerializerOptions { WriteIndented = true });
            //Console.WriteLine($"GameStateResponse: {JsonSerializer.Serialize(gsr, new JsonSerializerOptions { WriteIndented = true })}");
            return gsr;
        }

        private (Player player, bool isPlayerTurn) ValidatePlayerTurn(Game game, string playerId)
        {
            var player = game.Players.FirstOrDefault(p => p.Id == playerId);
            
            // If the player is not found but is in participants list, they've lost the game
            // Create a placeholder to avoid null reference exceptions
            if (player == null && game.ParticipantPlayerIds.Contains(playerId))
            {
                player = new Player 
                { 
                    Id = playerId,
                    GameId = game.Id,
                    IsActive = false,
                    PlayerIndex = -1, // Invalid index for a player who has lost
                    Characters = new List<Character>(),
                    Hand = new List<Card>()
                };
                return (player, false); // Lost players can never take a turn
            }
            
            if (player == null)
            {
                throw new ArgumentException($"Player with ID {playerId} not found in game {game.Id}");
            }

            // Check if player has already submitted their turn for the current round
            bool hasSubmittedTurn = game.SubmittedTurnPlayerIds.Contains(playerId);
            
            // Allow any active player who hasn't submitted their turn yet to take actions
            // Removed the CurrentPlayerIndex requirement so all players can submit turns in parallel
            bool isPlayerTurn = player.IsActive && !hasSubmittedTurn;
            
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

        private bool IsAttackerPlayer(Game game, Battle battle, string playerId)
        {
            var attackerCharacter = game.Players
                .SelectMany(p => p.Characters)
                .FirstOrDefault(c => c.Id == battle.AttackerCharacterId);
            
            return attackerCharacter != null && attackerCharacter.PlayerId == playerId;
        }

        private bool IsDefenderPlayer(Game game, Battle battle, string playerId)
        {
            var defenderCharacter = game.Players
                .SelectMany(p => p.Characters)
                .FirstOrDefault(c => c.Id == battle.DefenderCharacterId);
            
            return defenderCharacter != null && defenderCharacter.PlayerId == playerId;
        }

        private async Task ResolvePendingCardEffectsAsync(Game game, Player player)
        {
            // Get all cards played by this player that are pending resolution
            var pendingCards = await _gameRepository.GetPendingCardsForPlayerAsync(player.Id);
            
            foreach (var card in pendingCards)
            {
                // Apply card effects based on their type
                switch (card.CardType)
                {
                    case CardType.General:
                        // Apply general card effects
                        if (card.Effect.AdditionalMovement > 0 && !string.IsNullOrEmpty(card.TargetId))
                        {
                            // Find the target character
                            var targetCharacter = player.Characters.FirstOrDefault(c => c.Id == card.TargetId);
                            if (targetCharacter != null)
                            {
                                targetCharacter.MovementPoints += card.Effect.AdditionalMovement;
                                await _gameRepository.UpdateCharacterAsync(targetCharacter);
                            }
                        }
                        
                        // Apply permanent stat boosts
                        if (card.Effect.StatBonus > 0 && !string.IsNullOrEmpty(card.TargetId) &&
                            card.Effect.AffectsStat == "All")
                        {
                            var targetCharacter = player.Characters.FirstOrDefault(c => c.Id == card.TargetId);
                            if (targetCharacter != null)
                            {
                                targetCharacter.Melee += card.Effect.StatBonus;
                                targetCharacter.Magic += card.Effect.StatBonus;
                                targetCharacter.Diplomacy += card.Effect.StatBonus;
                                await _gameRepository.UpdateCharacterAsync(targetCharacter);
                            }
                        }
                        break;
                        
                    case CardType.Strategy:
                        // Strategy cards would be applied here
                        break;
                }
                
                // Mark card as resolved and remove it if it's a one-time effect
                if (card.Effect.EffectDuration <= 1)
                {
                    // One-time effect, remove card
                    await _gameRepository.RemoveCardAsync(card.Id);
                }
                else
                {
                    // Multi-turn effect, decrement duration and keep card
                    card.Effect.EffectDuration--;
                    card.PendingResolution = false;
                    await _gameRepository.UpdateCardAsync(card);
                }
            }
        }

        private async Task ResolveSubmittedBattlesAsync(Game game)
        {
            // Get all battles for this game where both players have submitted
            var pendingBattles = await _gameRepository.GetSubmittedBattlesAsync(game.Id);
            
            foreach (var battle in pendingBattles)
            {
                if (battle.AttackerSubmitted && battle.DefenderSubmitted && !battle.IsCompleted)
                {
                    Console.WriteLine($"Processing battle {battle.Id} between characters {battle.AttackerCharacterId} and {battle.DefenderCharacterId}");
                    
                    try
                    {
                        // Get the attacker and defender characters
                        var attacker = game.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == battle.AttackerCharacterId);
                        var defender = game.Players.SelectMany(p => p.Characters).FirstOrDefault(c => c.Id == battle.DefenderCharacterId);
                        
                        if (attacker == null || defender == null)
                        {
                            Console.WriteLine($"Warning: Could not find characters for battle {battle.Id}. Skipping battle resolution.");
                            continue;
                        }
                        
                        // Store initial scores for battle history
                        int initialAttackerScore = battle.AttackerScore;
                        int initialDefenderScore = battle.DefenderScore;
                        
                        // Process cards played by attacker
                        List<string> attackerCardEffects = new List<string>();
                        foreach (var cardId in battle.AttackerCardsPlayed)
                        {
                            var card = await _gameRepository.GetCardAsync(cardId);
                            if (card != null)
                            {
                                // Apply card effects
                                string effectDescription = ProcessBattleCard(battle, card, attacker.PlayerId);
                                if (!string.IsNullOrEmpty(effectDescription))
                                {
                                    attackerCardEffects.Add(effectDescription);
                                }
                            }
                        }
                        
                        // Process cards played by defender
                        List<string> defenderCardEffects = new List<string>();
                        foreach (var cardId in battle.DefenderCardsPlayed)
                        {
                            var card = await _gameRepository.GetCardAsync(cardId);
                            if (card != null)
                            {
                                // Apply card effects
                                string effectDescription = ProcessBattleCard(battle, card, defender.PlayerId);
                                if (!string.IsNullOrEmpty(effectDescription))
                                {
                                    defenderCardEffects.Add(effectDescription);
                                }
                            }
                        }
                        
                        // Determine winner
                        bool attackerWins = battle.AttackerScore > battle.DefenderScore;
                        
                        // Create battle history to be stored
                        var battleHistory = new BattleHistoryRecord
                        {
                            BattleId = battle.Id,
                            GameId = battle.GameId,
                            BattleType = battle.BattleType.ToString(),
                            AttackerCharacterId = battle.AttackerCharacterId,
                            DefenderCharacterId = battle.DefenderCharacterId,
                            InitialAttackerScore = initialAttackerScore,
                            InitialDefenderScore = initialDefenderScore,
                            FinalAttackerScore = battle.AttackerScore,
                            FinalDefenderScore = battle.DefenderScore,
                            TerrainBonus = battle.TerrainBonus,
                            AttackerCardEffects = attackerCardEffects,
                            DefenderCardEffects = defenderCardEffects,
                            Winner = attackerWins ? "Attacker" : "Defender",
                            WinnerId = attackerWins ? attacker.PlayerId : defender.PlayerId,
                            CompletedAt = DateTime.UtcNow
                        };
                        
                        // Store battle history in battle object (serialized as JSON)
                        battle.BattleHistory = JsonSerializer.Serialize(battleHistory);
                        
                        // Set the winner
                        battle.WinnerId = attackerWins ? attacker.PlayerId : defender.PlayerId;
                        
                        // Now resolve the actual battle outcome
                        await ResolveBattleAsync(game, battle);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error resolving battle {battle.Id}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                        }
                    }
                }
            }
        }

        // Centralized function to check and resolve end-of-turn conditions
        private async Task<bool> ResolveEndOfTurn(Game game)
        {
            // Check if all active players have submitted their turns
            bool allPlayersSubmitted = true;
            foreach (var activePlayer in game.Players.Where(p => p.IsActive))
            {
                if (!game.SubmittedTurnPlayerIds.Contains(activePlayer.Id))
                {
                    allPlayersSubmitted = false;
                    break;
                }
            }

            // If all players have submitted, resolve the turn
            if (allPlayersSubmitted)
            {
                Console.WriteLine($"All players submitted their turns for turn {game.CurrentTurn}. Resolving end of turn.");
                
                // Resolve all battles where both players have submitted
                await ResolveSubmittedBattlesAsync(game);
                
                // Increment the turn counter
                game.CurrentTurn++;
                Console.WriteLine($"Starting new turn {game.CurrentTurn}");
                
                // Reset the submitted player list for the new turn
                game.SubmittedTurnPlayerIds.Clear();
                
                // Reset to the first player's turn
                game.CurrentPlayerIndex = 0;
                
                // Update the game state
                await _gameRepository.UpdateGameAsync(game);
                return true;
            }
            
            return false;
        }

        private string ProcessBattleCard(Battle battle, Card card, string playerId)
        {
            var isAttacker = battle.AttackerCharacterId == playerId;
            string effectDescription = "";
            
            // Apply battle bonus if card has one and affects the correct stat type
            if (card.Effect.BattleBonus > 0)
            {
                bool shouldApply = false;
                
                // Check if the battle type matches the card's affected stat
                if (string.IsNullOrEmpty(card.Effect.AffectsStat) || 
                    card.Effect.AffectsStat == "All" ||
                    (card.Effect.AffectsStat == "Melee" && battle.BattleType == BattleType.Melee) ||
                    (card.Effect.AffectsStat == "Magic" && battle.BattleType == BattleType.Magic) ||
                    (card.Effect.AffectsStat == "Diplomacy" && battle.BattleType == BattleType.Diplomacy))
                {
                    shouldApply = true;
                }
                
                if (shouldApply)
                {
                    if (isAttacker)
                    {
                        battle.AttackerScore += card.Effect.BattleBonus;
                        effectDescription = $"+{card.Effect.BattleBonus} to attack score from {card.Name}";
                    }
                    else
                    {
                        battle.DefenderScore += card.Effect.BattleBonus;
                        effectDescription = $"+{card.Effect.BattleBonus} to defense score from {card.Name}";
                    }
                }
            }
            
            // Apply defensive bonus if card has one (defender only)
            if (card.Effect.DefensiveBonus > 0 && !isAttacker)
            {
                battle.DefenderScore += card.Effect.DefensiveBonus;
                effectDescription = $"+{card.Effect.DefensiveBonus} to defense score from {card.Name} (defensive bonus)";
            }
            
            // Apply terrain negation if applicable
            if (card.Effect.NegateTerrainBonus)
            {
                // Only affect terrain bonus if it exists and the card is played by the attacker
                if (battle.TerrainBonus > 0 && isAttacker)
                {
                    var originalBonus = battle.TerrainBonus;
                    battle.DefenderScore -= battle.TerrainBonus;
                    battle.TerrainBonus = 0;
                    effectDescription = $"Negated defender's terrain bonus of {originalBonus} with {card.Name}";
                }
            }
            
            return effectDescription;
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

// Class to track battle history details for UI display
public class BattleHistoryRecord
{
    public string BattleId { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string BattleType { get; set; } = string.Empty;
    public string AttackerCharacterId { get; set; } = string.Empty;
    public string DefenderCharacterId { get; set; } = string.Empty;
    public int InitialAttackerScore { get; set; }
    public int InitialDefenderScore { get; set; }
    public int FinalAttackerScore { get; set; }
    public int FinalDefenderScore { get; set; }
    public int TerrainBonus { get; set; }
    public List<string> AttackerCardEffects { get; set; } = new List<string>();
    public List<string> DefenderCardEffects { get; set; } = new List<string>();
    public string Winner { get; set; } = string.Empty;
    public string WinnerId { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
}