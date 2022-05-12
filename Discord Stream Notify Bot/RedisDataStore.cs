using Google.Apis.Util.Store;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot
{
    public class RedisDataStore : IDataStore
    {
        private readonly IDatabase _database;

        public RedisDataStore(ConnectionMultiplexer connectionMultiplexer)
        {
            _database = connectionMultiplexer.GetDatabase(1);
        }

        public Task ClearAsync()
        {
            throw new NotImplementedException();
        }

        public async Task DeleteAsync<T>(string key)
        {
            await _database.KeyDeleteAsync(GenerateStoredKey(key, typeof(T)));
        }

        public async Task<T> GetAsync<T>(string key)
        {
            if (!await _database.KeyExistsAsync(GenerateStoredKey(key, typeof(T))))
                return default(T);

            var str = await _database.StringGetAsync(GenerateStoredKey(key, typeof(T)));
            var result = JsonConvert.DeserializeObject<T>(str.ToString());
            return result;
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            await _database.StringSetAsync(GenerateStoredKey(key, typeof(T)), JsonConvert.SerializeObject(value));
        }

        public static string GenerateStoredKey(string key, Type t)
        {
            return string.Format("{0}:{1}", t.FullName, key);
        }
    }
}
