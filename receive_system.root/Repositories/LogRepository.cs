using receive_system.core.Interfaces.Repositories;
using receive_system.root.Context;

namespace receive_system.root.Repositories
{
    public class LogRepository : ILogRepository
    {
        private readonly MongoContext _context;

        public LogRepository(MongoContext context)
        {
            _context = context;
        }

        public async Task InsertLogAsync<T>(T log, string collectionName, string databaseName)
        {
            var collection = _context.GetCollection<T>(collectionName, databaseName);
            await collection.InsertOneAsync(log);
        }
    }
}