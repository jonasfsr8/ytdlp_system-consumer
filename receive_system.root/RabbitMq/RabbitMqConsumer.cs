using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using receive_system.core.Entities;
using receive_system.core.Interfaces.Messages;
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

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());

                var message = JsonConvert.DeserializeObject<Envelope>(body);

                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler>();

                try
                {
                    await handler.HandleAsync(message);

                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                }
                catch (Exception)
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, false, true);
                }
            };

            await channel.BasicConsumeAsync(
                queue: _config.QueueName,
                autoAck: false,
                consumer: consumer);
        }
    }
}
