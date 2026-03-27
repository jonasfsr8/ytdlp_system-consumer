using receive_system.core.Entities;
using receive_system.core.Interfaces.Repositories;
using receive_system.core.Interfaces.Messages;

namespace receive_system.module.Handler
{
    public class MessageHandler : IMessageHandler
    {
        public async Task HandleAsync(Envelope message)
        {
            throw new NotImplementedException();
        }
    }
}
