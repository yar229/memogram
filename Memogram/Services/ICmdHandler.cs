using Telegram.Bot.Types;

namespace Memogram.Services;

public interface ICmdHandler
{
    string Command { get; }

    string Usage { get; }

    Task Handle(Message message, string args, CancellationToken ct);
}
