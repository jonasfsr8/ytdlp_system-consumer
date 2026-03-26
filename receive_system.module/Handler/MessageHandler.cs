using receive_system.core.Entities;
using receive_system.core.Interfaces.Repositories;
using receive_system.core.Interfaces.Messages;

namespace receive_system.module.Handler
{
    public class MessageHandler : IMessageHandler
    {
        private readonly ILogRepository _logRepository;

        public MessageHandler(ILogRepository logRepository)
        {
            _logRepository = logRepository;
        }

        public async Task HandleAsync(Envelope message)
        {
            await _logRepository.InsertLogAsync(message, "youtube_dl", "messages");
        }
    }
}
