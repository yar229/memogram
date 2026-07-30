using Telegram.Bot;
using Telegram.Bot.Types;

namespace Memogram.Services.Telegram;

class BotCommandHandler
{
    public BotCommand Command { get; set; }
    public Func<Message, string, CancellationToken, Task> Handler { get; set; }
}

