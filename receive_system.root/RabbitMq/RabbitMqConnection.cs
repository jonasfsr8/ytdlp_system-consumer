using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using receive_system.root.DTOs;
using receive_system.root.Interfaces;

namespace receive_system.root.RabbitMq
{
    public class RabbitMqConnection : IRabbitMqConnection
    {
        private readonly ConnectionFactory _factory;
        private IConnection? _connection;
        private readonly ILogger<RabbitMqConnection> _logger;

        public RabbitMqConnection(IOptions<RabbitMqConfigDto> config, ILogger<RabbitMqConnection> logger)
        {
            _logger = logger;
            var settings = config.Value;

            _factory = new ConnectionFactory
            {
                HostName = settings.HostName,
                UserName = settings.UserName,
                Password = settings.Password,
                Port = settings.Port,
            };
        }

        private async Task<IConnection> GetConnectionAsync()
        {
            if (_connection is not null && _connection.IsOpen)
                return _connection;

            try
            {
                _connection = await _factory.CreateConnectionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[*] error connecting to rabbitmq");
            }

            return _connection;
        }

        public async Task<IChannel> CreateChannel()
        {
            var connection = await GetConnectionAsync();
            return await connection.CreateChannelAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                if (_connection.IsOpen)
                    await _connection.CloseAsync();

                _connection.Dispose();
            }
        }
    }
}
