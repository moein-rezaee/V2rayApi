using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using V2rayApi.Models;

namespace V2rayApi.Services;

public class TelegramBotService
{
    private readonly TelegramBotClient _bot;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IConfiguration _config;
    private readonly XuiService _xuiService;
    private readonly ConcurrentDictionary<long, Plan> _userPlans = new();

    private long AdminChatId => long.Parse(_config["Telegram:AdminChatId"] ?? "0");
    private string CardNumber => _config["Payment:CardNumber"] ?? string.Empty;
    private string WalletAddress => _config["Payment:WalletAddress"] ?? string.Empty;

    public TelegramBotService(IConfiguration config, ILogger<TelegramBotService> logger, XuiService xuiService)
    {
        _config = config;
        _logger = logger;
        _xuiService = xuiService;
        var token = _config["Telegram:Token"] ?? throw new ArgumentNullException("Telegram token not configured");
        _bot = new TelegramBotClient(token);
    }

    public async Task HandleUpdateAsync(Update update)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message != null)
            {
                await HandleMessage(update.Message);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallback(update.CallbackQuery);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing update");
        }
    }

    private async Task HandleMessage(Message message)
    {
        if (message.Text == "/start")
        {
            await SendPlanOptions(message.Chat.Id);
        }
        else if (message.Photo?.Any() == true)
        {
            await HandleReceipt(message);
        }
    }

    private async Task SendPlanOptions(long chatId)
    {
        var plans = _config.GetSection("Plans").Get<List<Plan>>() ?? new();
        var buttons = plans.Select(p => new[] { InlineKeyboardButton.WithCallbackData($"{p.Name} - {p.Price}", $"plan:{p.Id}") });
        await _bot.SendMessage(chatId, "یک طرح را انتخاب کنید:", replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task HandleCallback(CallbackQuery query)
    {
        if (query.Data == null) return;
        if (query.Data.StartsWith("plan:"))
        {
            var planId = query.Data.Split(':')[1];
            var plan = _config.GetSection("Plans").Get<List<Plan>>()?.FirstOrDefault(p => p.Id == planId);
            if (plan != null)
            {
                _userPlans[query.From.Id] = plan;
                await _bot.SendMessage(query.Message!.Chat.Id, $"هزینه {plan.Price} را به کارت {CardNumber}\nیا کیف پول {WalletAddress} واریز کرده و رسید را ارسال کنید.",
                    replyMarkup: new ReplyKeyboardMarkup(new[] { new KeyboardButton("ارسال رسید") }) { ResizeKeyboard = true });
            }
        }
        else if (query.Data.StartsWith("approve:"))
        {
            var userId = long.Parse(query.Data.Split(':')[1]);
            if (_userPlans.TryGetValue(userId, out var plan))
            {
                var (link, qr) = await _xuiService.CreateInboundAsync(userId, plan);
                await _bot.SendMessage(userId, $"کانفیگ شما:\n{link}");
                using var ms = new MemoryStream(qr);
                await _bot.SendPhoto(userId, InputFile.FromStream(ms, "qr.png"));
            }
            await _bot.SendMessage(query.From.Id, "تایید شد");
        }
        else if (query.Data.StartsWith("reject:"))
        {
            var userId = long.Parse(query.Data.Split(':')[1]);
            await _bot.SendMessage(userId, "رسید شما رد شد. لطفا مجدد اقدام کنید.");
            await _bot.SendMessage(query.From.Id, "رد شد");
        }
        await _bot.AnswerCallbackQuery(query.Id);
    }

    private async Task HandleReceipt(Message message)
    {
        var photo = message.Photo!.Last();
        var file = await _bot.GetFile(photo.FileId);
        Directory.CreateDirectory("receipts");
        var path = Path.Combine("receipts", $"{message.From!.Id}_{DateTime.UtcNow.Ticks}.jpg");
        await using (var fs = new FileStream(path, FileMode.Create))
        {
            await _bot.DownloadFile(file.FilePath!, fs);
        }
        var caption = $"User: {message.From.Id} @{message.From.Username}\n" +
                      (_userPlans.TryGetValue(message.From.Id, out var plan) ? $"Plan: {plan.Name}" : "");
        using var stream = System.IO.File.OpenRead(path);
        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("تایید", $"approve:{message.From.Id}"),
                InlineKeyboardButton.WithCallbackData("رد", $"reject:{message.From.Id}")
            }
        });
        await _bot.SendPhoto(AdminChatId, InputFile.FromStream(stream, Path.GetFileName(path)), caption: caption, replyMarkup: buttons);
        await _bot.SendMessage(message.Chat.Id, "رسید دریافت شد. منتظر تایید بمانید.");
    }
}
