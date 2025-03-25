using Supabase;
using HexGame.API.Models;

namespace HexGame.API.Services
{
    public class SupabaseService : ISupabaseService
    {
        private readonly Client _client;

        public string Url { get; }
        public string Key { get; }

        public SupabaseService(string url, string key)
        {
            Url = url;
            Key = key;
            
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true
            };
            
            _client = new Client(url, key, options);

            // Register models
            _client.From<Game>();
            _client.From<Player>();
            _client.From<Character>();
            _client.From<Hex>();
            _client.From<Card>();
            _client.From<Battle>();
        }

        public Client GetClient()
        {
            return _client;
        }
    }
}
