namespace receive_system.core.Interfaces.Repositories
{
    public interface ILogRepository
    {
        Task InsertLogAsync<T>(T log, string collectionName, string databaseName);
    }
}
