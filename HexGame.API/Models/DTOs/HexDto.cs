namespace HexGame.API.Models.DTOs
{
    public class HexDto
    {
        public string Id { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public int Q { get; set; }
        public int R { get; set; }
        public int S { get; set; }
        public bool IsExplored { get; set; } = false;
        public int TerrainRating { get; set; } = 1;
        public int ResourceIndustry { get; set; } = 0;
        public int ResourceAgriculture { get; set; } = 0;
        public int ResourceBuilding { get; set; } = 0;
        public string? OwnerId { get; set; }
        public List<string> ExploredBy { get; set; } = new List<string>();
    }
}