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
        private readonly RabbitMqSettingsDto _config;
        private readonly ILogger<RabbitMqConsumer> _logger;

        public RabbitMqConsumer(
            IRabbitMqConnection connection,
            IServiceScopeFactory scopeFactory,
            IOptions<RabbitMqSettingsDto> config,
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

                // Controle de carga
                await channel.BasicQosAsync(0, 1, false);

                var mainQueue = _config.QueueName;
                var retryQueue = $"{mainQueue}-retry";
                var deadQueue = $"{mainQueue}-dead";

                // Main queue
                await channel.QueueDeclareAsync(
                    queue: mainQueue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false);

                // Dead-letter queue
                await channel.QueueDeclareAsync(
                    queue: deadQueue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        { "x-dead-letter-exchange", "" },
                        { "x-dead-letter-routing-key", deadQueue }
                    });

                // Retry queue
                await channel.QueueDeclareAsync(
                    queue: retryQueue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        { "x-message-ttl", 5000 },
                        { "x-dead-letter-exchange", "" },
                        { "x-dead-letter-routing-key", mainQueue }
                    });

                var consumer = new AsyncEventingBasicConsumer(channel);

                consumer.ReceivedAsync += async (sender, ea) =>
                {
                    if (stoppingToken.IsCancellationRequested)
                        return;

                    var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                    var message = JsonConvert.DeserializeObject<Envelope>(body);

                    if (message is null || string.IsNullOrWhiteSpace(message.Payload))
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
                        {
                            await logRepository.InsertLogAsync(message, "youtube_tasks", "messages");
                        }

                        await handler.HandleAsync(message);

                        _logger.LogInformation("[*] message processed successfully");

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
                        else
                        {
                            await channel.BasicPublishAsync(
                                exchange: "",
                                routingKey: deadQueue,
                                body: Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message)));
                        }

                        await channel.BasicAckAsync(ea.DeliveryTag, false);
                    }
                };

                await channel.BasicConsumeAsync(
                    queue: mainQueue,
                    autoAck: false,
                    consumer: consumer);

                // mantém o serviço vivo
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "[*] critical error in consumer");
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
