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

    private static ReplyKeyboardMarkup BuildMainKeyboard()
    {
        return new ReplyKeyboardMarkup([
            [
                // new KeyboardButton("💵 خرید کانفیگ ")
                new KeyboardButton("👨‍💻 پشتیبانی"),
                new KeyboardButton("🎉 خرید کانفیگ جشنواره"),
            ]
        ])
        {
            ResizeKeyboard = true,      // سایز بهینه
            OneTimeKeyboard = false,    // بعد از کلیک مخفی نشه
                                        // IsPersistent = true,     // اگر نسخه‌ی کتابخانه‌ات پشتیبانی می‌کند، کیبورد را پایدار کن
            InputFieldPlaceholder = "لطفا یکی از گزینه های زیر را انتخاب کنید 👇"
        };
    }

    private static ReplyKeyboardMarkup BuildAdminKeyboard()
    {
        return new ReplyKeyboardMarkup([
            [
                new KeyboardButton("⬆️ ارسال کانفیگ")
            ]
        ])
        {
            ResizeKeyboard = true,      // سایز بهینه
            OneTimeKeyboard = false,    // بعد از کلیک مخفی نشه
                                        // IsPersistent = true,     // اگر نسخه‌ی کتابخانه‌ات پشتیبانی می‌کند، کیبورد را پایدار کن
            InputFieldPlaceholder = "لطفا یکی از گزینه های زیر را انتخاب کنید 👇"
        };
    }

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
        if (message.Text == "/start" && message.Chat.Id != AdminChatId)
        {
            var chatId = message.Chat.Id;
            await _bot.SendMessage(
                chatId,
                "🌟 به بات نت‌کی خوش اومدی! از منوی زیر یکی از گزینه‌ها رو انتخاب کن:",
                replyMarkup: BuildMainKeyboard()
            );
        }
        if (message.Text == "/start" && message.Chat.Id == AdminChatId)
        {
            await _bot.SendMessage(message!.Chat.Id, @"🌸✨
همکار عزیز، پشتیبان محترم نت‌کی
به بات پشتیبانی خوش اومدی ☺️💙
امیدوارم تجربه‌ای راحت و سریع داشته باشی 🙌"
            );
        }
        else if (message.Photo?.Any() == true)
        {
            await HandleReceipt(message);
        }
        else if (message.Text.Contains("خرید کانفیگ") && message.Chat.Id != AdminChatId)
        {
            await SendPlanOptions(message.Chat.Id);
        }
        else if (message.Text.Contains("پشتیبانی") && message.Chat.Id != AdminChatId)
        {
            await _bot.SendMessage(message.Chat.Id, @"👨‍💻 پشتیبانی نت‌کی
برای ارتباط سریع: @NetKeySupport
ایمیل: netkey.v2ray@gmail.com

📝 لطفاً هنگام پیام این موارد را بفرستید:
• شماره پیگیری
• طرح انتخابی
• توضیح کوتاه مشکل/درخواست
تا سریع‌تر رسیدگی کنیم 🙏");
        }
    }

    private async Task SendPlanOptions(long chatId)
    {
        var plans = _config.GetSection("SpecialPlans").Get<List<Plan>>() ?? new();
        // var plans = _config.GetSection("Plans").Get<List<Plan>>() ?? new();
        var buttons = plans.Select(p => new[] { InlineKeyboardButton.WithCallbackData($"{p.Name} - {p.Price} هزار تومان", $"plan:{p.Id}") });
        //         await _bot.SendMessage(chatId, @"همراه گرامی نت کی 🌹
        // تعرفه‌های نت‌کی خدمتتون ارسال شد.
        // لطفاً یکی از طرح‌ها رو انتخاب بفرمایید تا همکاران ما در نت کی در سریع‌ترین زمان فعال‌سازی طرح شما رو انجام بدن.",
        // replyMarkup: new InlineKeyboardMarkup(buttons));
        await _bot.SendMessage(chatId, @"به جشنواره فروش نت کی خوش اومدید 🎉
لطفاً یکی از طرح‌ها رو انتخاب بفرمایید تا همکاران ما در نت کی در سریع‌ترین زمان فعال‌سازی طرح شما رو انجام بدن.",
        replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task SendPlanOptionsAgain(long chatId)
    {
        var plans = _config.GetSection("SpecialPlans").Get<List<Plan>>() ?? new();
        // var plans = _config.GetSection("Plans").Get<List<Plan>>() ?? new();
        var buttons = plans.Select(p => new[] { InlineKeyboardButton.WithCallbackData($"{p.Name} - {p.Price} هزار تومان", $"plan:{p.Id}") });
        await _bot.SendMessage(chatId, @"همراه گرامی نت کی 🌹
سپاس از پرداخت تون 🙏
لطفا انتخاب کنید که رسید ارسالی بابت کدام یکی از طرح های ماست سپس مجددا رسید رو ارسال کنید.",
replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task HandleCallback(CallbackQuery query)
    {
        if (query.Data == null) return;
        if (query.Data.StartsWith("plan:"))
        {
            var planId = query.Data.Split(':')[1];
            var plan = _config.GetSection("SpecialPlans").Get<List<Plan>>()?.FirstOrDefault(p => p.Id == planId);
            if (plan != null)
            {
                _userPlans[query.From.Id] = plan;
                await _bot.SendMessage(query.Message!.Chat.Id, $@"✅ طرح انتخابی شما: {plan.Description}.

💳 لطفاً مبلغ {plan.Price} هزار تومان را جهت تکمیل فرایند به کارت زیر واریز فرمایید و رسید را ارسال کنید:

6219861070956510

⚠️ نکته مهم:
انتخاب طرح به معنی نهایی شدن آن نیست. شما می‌توانید هر تعداد بار طرح خود را تغییر دهید.
تا زمانی که رسید پرداخت ارسال نشود، آخرین طرح انتخابی شما به عنوان طرح فعال در نظر گرفته می‌شود.");
            }
        }
        else if (query.Data.StartsWith("approve:"))
        {
            var userId = long.Parse(query.Data.Split(':')[1]);
            // if (_userPlans.TryGetValue(userId, out var plan))
            // {
            //     var (link, qr) = await _xuiService.CreateInboundAsync(userId, plan);
            //     await _bot.SendMessage(userId, $"کانفیگ شما:\n{link}");
            //     using var ms = new MemoryStream(qr);
            //     await _bot.SendPhoto(userId, InputFile.FromStream(ms, "qr.png"));
            // }
            // await _bot.SendMessage(query.From.Id, "تایید شد");

            await _bot.SendMessage(userId, @$"✅ کاربر عزیز، رسید شما تایید شد.
برای دریافت کانفیگ خود لطفاً با کد رهگیری به پشتیبانی مراجعه کنید 🙏

💻 پشتیبانی نت کی: 
@NetKeySupport

🆔 کد رهگیری: 
{userId}");
            await _bot.SendMessage(query.From.Id, @"📌 همکار گرامی، رسیدی که تایید کردید با موفقیت ثبت و به کاربر اطلاع داده شد.
⚠️ لطفاً توجه داشته باشید که مشتری جهت پیگیری با کد رهگیری به شما مراجعه خواهد کرد؛ لطفا لینک کاربر آماده تحویل باشه و در دسترس باشید.");


        }
        else if (query.Data.StartsWith("reject:"))
        {
            var userId = long.Parse(query.Data.Split(':')[1]);
            await _bot.SendMessage(userId, @$"⚠️ کاربر عزیز، رسید شما رد شد.
برای پیگیری دلیل رد، لطفاً با کد رهگیری به پشتیبانی مراجعه کنید 🙏

💻 پشتیبانی نت کی: 
@NetKeySupport

🆔 کد رهگیری: 
{userId}");
            await _bot.SendMessage(query.From.Id, @"📌 همکار گرامی، رسیدی که رد کردید با موفقیت ثبت و به کاربر اطلاع داده شد.
⚠️ لطفاً توجه داشته باشید که مشتری جهت پیگیری با کد رهگیری به شما مراجعه خواهد کرد؛ لطفا در دسترس باشید.");
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
        _userPlans.TryGetValue(message.From.Id, out var plan);
        if (plan is not null)
        {
            var caption = $"User: {message.From.Id} @{message.From.Username}\nPlan: {plan.Name}";
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
            await _bot.SendMessage(message.Chat.Id, @"🙏 با تشکر از اعتماد شما
📩 رسید با موفقیت دریافت شد.
لطفاً تا تأیید نهایی توسط همکاران عزیز ما در نت‌کی شکیبا باشید 🌸");
        }
        else
        {
            await SendPlanOptionsAgain(message.Chat.Id);
        }

    }
}
