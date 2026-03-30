using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using receive_system.core.Entities;
using receive_system.core.Interfaces.Messages;
using receive_system.core.Interfaces.Repositories;
using receive_system.root.DTOs;
using receive_system.root.Interfaces;
using System.Text;

namespace receive_system.root.RabbitMq
{
    public class RabbitMqConsumer : BackgroundService
    {
        private readonly IRabbitMqConnection _connection;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly RabbitMqConfigDto _config;
        private readonly ILogger<RabbitMqConsumer> _logger;

        public RabbitMqConsumer(
            IRabbitMqConnection connection,
            IServiceScopeFactory scopeFactory,
            IOptions<RabbitMqConfigDto> config,
            ILogger<RabbitMqConsumer> logger)
        {
            _connection = connection;
            _scopeFactory = scopeFactory;
            _config = config.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("[*] starting rabbitmq consumer...");

                var channel = await _connection.CreateChannel();
                _logger.LogInformation("[*] channel created successfully");

                await channel.QueueDeclareAsync(
                    queue: _config.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?> { { "x-queue-type", "classic" } });

                _logger.LogInformation("[*] main queue declared: {queue}", _config.QueueName);

                await channel.QueueDeclareAsync(
                    queue: $"{_config.QueueName}-retry",
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                { "x-message-ttl", 5000 },
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", _config.QueueName }
                    });

                _logger.LogInformation("[*] retry queue created: {retryQueue}", $"{_config.QueueName}-retry");

                var consumer = new AsyncEventingBasicConsumer(channel);

                _logger.LogInformation("[*] awaiting messages...");

                consumer.ReceivedAsync += async (sender, ea) =>
                {
                    _logger.LogInformation("[*] message received (deliverytag: {tag})", ea.DeliveryTag);

                    var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var message = JsonConvert.DeserializeObject<Envelope>(body);

                    if (message == null)
                    {
                        _logger.LogWarning("[*] invalid message (null). ignoring...");
                        await channel.BasicAckAsync(ea.DeliveryTag, false);
                        return;
                    }

                    using var scope = _scopeFactory.CreateScope();

                    var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler>();
                    var logRepository = scope.ServiceProvider.GetRequiredService<ILogRepository>();

                    try
                    {
                        _logger.LogInformation("[*] processing message | retrycount: {retry}", message.RetryCount);

                        if (message.RetryCount == 0)
                        {
                            _logger.LogInformation("[*] inserting initial log into the database");
                            await logRepository.InsertLogAsync(message, "youtube_tasks", "messages");
                        }

                        await handler.HandleAsync(message);

                        _logger.LogInformation("[*] message processed successfully");

                        await channel.BasicAckAsync(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        const int maxRetry = 3;

                        _logger.LogError(ex, "[*] error processing message | retrycount current: {retry}", message.RetryCount);

                        if (message.RetryCount < maxRetry)
                        {
                            message.RetryCount++;

                            _logger.LogWarning("[*] sending to retry #{retry}", message.RetryCount);

                            await logRepository.UpdateLogAsync(message, "youtube_tasks", "messages");

                            await RetryMessage(channel, message);
                        }
                        else
                        {
                            _logger.LogError("[*] this message has reached its retrieval limit. ({maxRetry})", maxRetry);
                        }

                        await channel.BasicAckAsync(ea.DeliveryTag, false);
                    }
                };

                await channel.BasicConsumeAsync(
                    queue: _config.QueueName,
                    autoAck: false,
                    consumer: consumer);

                _logger.LogInformation("[*] active consumer listening in the queue.: {queue}", _config.QueueName);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, $"[*] {ex.Message}");
            }
        }

        private async Task RetryMessage(IChannel channel, Envelope message)
        {
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: $"{_config.QueueName}-retry",
                body: body);
        }
    }
}
