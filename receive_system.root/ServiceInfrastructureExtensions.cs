using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using receive_system.core.Interfaces.Repositories;
using receive_system.root.Context;
using receive_system.root.DTOs;
using receive_system.root.Interfaces;
using receive_system.root.RabbitMq;
using receive_system.root.Repositories;

namespace receive_system.root
{
    public static class ServiceInfrastructureExtensions
    {
        public static IServiceCollection ConfigurePersistenceApp(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<MongoDbConfigDto>(configuration.GetSection("MongoDb"));

            services.AddSingleton<IMongoClient>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<MongoDbConfigDto>>().Value;
                return new MongoClient(config.ConnectionString);
            });

            services.AddScoped<MongoContext>();
            services.Configure<RabbitMqConfigDto>(configuration.GetSection("RabbitMqConfig"));
            services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
            services.AddHostedService<RabbitMqConsumer>();
            services.AddScoped<ILogRepository, LogRepository>();

            return services;
        }
    }
}
