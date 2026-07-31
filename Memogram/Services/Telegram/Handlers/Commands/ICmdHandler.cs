using Telegram.Bot.Types;

namespace Memogram.Services.Telegram.Handlers.Commands;

public interface ICmdHandler
{
    string Command { get; }

    string Usage { get; }

    Task Handle(Message message, string args, CancellationToken ct);
}
