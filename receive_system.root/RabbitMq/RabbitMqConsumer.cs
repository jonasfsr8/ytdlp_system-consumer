using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        public RabbitMqConsumer(
            IRabbitMqConnection connection,
            IServiceScopeFactory scopeFactory,
            IOptions<RabbitMqConfigDto> config)
        {
            _connection = connection;
            _scopeFactory = scopeFactory;
            _config = config.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var channel = await _connection.CreateChannel();

            await channel.QueueDeclareAsync(
                queue: _config.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?> { { "x-queue-type", "classic" } });

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

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                var message = JsonConvert.DeserializeObject<Envelope>(body);

                if (message == null)
                {
                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();

                var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler>();
                var logRepository = scope.ServiceProvider.GetRequiredService<ILogRepository>();

                try
                {
                    if (message.RetryCount == 0)
                        await logRepository.InsertLogAsync(message, "youtube_tasks", "messages");

                    await handler.HandleAsync(message);

                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    const int maxRetry = 3;

                    if (message.RetryCount < maxRetry)
                    {
                        message.RetryCount++;

                        await logRepository.UpdateLogAsync(message, "youtube_tasks", "messages");

                        await RetryMessage(channel, message);
                    }

                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                }
            };

            await channel.BasicConsumeAsync(
                queue: _config.QueueName,
                autoAck: false,
                consumer: consumer);
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
