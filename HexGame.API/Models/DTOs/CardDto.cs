namespace HexGame.API.Models.DTOs
{
    public class CardDto
    {
        public string Id { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string PlayerId { get; set; } = string.Empty;
        public CardType CardType { get; set; }
        public string CardDefinitionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CardEffectDto Effect { get; set; } = new CardEffectDto();
        public bool PendingResolution { get; set; } = false;
        public string? TargetId { get; set; }
        public int? PlayedOnTurn { get; set; }
    }

    public class CardEffectDto
    {
        public int StatBonus { get; set; } = 0;
        public string? AffectsStat { get; set; }
        public bool NegateTerrainBonus { get; set; } = false;
        public int AdditionalMovement { get; set; } = 0;
        public int BattleBonus { get; set; } = 0;
        public int DefensiveBonus { get; set; } = 0;
        public int EffectDuration { get; set; } = 1;
    }
}