using Supabase;

namespace HexGame.API.Services
{
    public interface ISupabaseService
    {
        Client GetClient();
        string Url { get; }
        string Key { get; }
    }
}
