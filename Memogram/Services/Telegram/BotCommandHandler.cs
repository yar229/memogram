using Telegram.Bot;
using Telegram.Bot.Types;

namespace Memogram.Services.Telegram;

class BotCommandHandler
{
    public required BotCommand Command { get; init; }
    public required Func<Message, string, CancellationToken, Task> Handler { get; init; }
}

