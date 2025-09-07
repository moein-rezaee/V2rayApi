using System.Collections.Concurrent;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using V2rayApi.Models;
using QRCoder;

namespace V2rayApi.Services;

public class TelegramBotService
{
    private readonly TelegramBotClient _bot;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<long, Plan> _userPlans = new();
    private readonly List<ChannelInfo> _requiredChannels;
    private readonly string ACTIVE_PLAN = "Plans";
    // private readonly string ACTIVE_PLAN = "SpecialPlans";

    private long AdminChatId => long.Parse(_config["Telegram:AdminChatId"] ?? "0");
    private string CardNumber => _config["Payment:CardNumber"] ?? string.Empty;
    private string WalletAddress => _config["Payment:WalletAddress"] ?? string.Empty;

    private static ReplyKeyboardMarkup BuildMainKeyboard()
    {
        return new ReplyKeyboardMarkup([
            [
                new KeyboardButton("💵 خرید کانفیگ "),
                // new KeyboardButton("🎉 خرید کانفیگ جشنواره"),
            ],
            [
                new KeyboardButton("👨‍💻 پشتیبانی"),
                new KeyboardButton("📩 دانلود نرم افزارها"),
            ],
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

    private async Task<bool> IsUserMemberOfRequiredChannels(long userId)
    {
        foreach (var channel in _requiredChannels)
        {
            try
            {
                var username = channel.Username?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(username)) continue;
                if (!username.StartsWith("@")) username = $"@{username}";
                var chatId = new ChatId(username);
                var member = await _bot.GetChatMember(chatId, userId);

                if (member.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
                {
                    return false;
                }
            }
            catch (ApiRequestException ex)
            {
                _logger.LogWarning(ex, "Cannot check membership for {Channel}", channel.Username);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while checking membership for {Channel}", channel.Username);
                return false;
            }
        }

        return true;
    }

    private async Task SendJoinChannelsPrompt(long userId)
    {
        var buttons = _requiredChannels
            .Select(c => new[]
            {
                InlineKeyboardButton.WithUrl(c.Title, $"https://t.me/{c.Username.TrimStart('@')}")
            })
            .ToList();

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("✅ عضو شدم", "joined") });

        await _bot.SendMessage(
            userId,
            "برای استفاده از ربات، لطفاً ابتدا در کانال‌های زیر عضو شوید و سپس روی دکمه «عضو شدم» بزنید 📢",
            replyMarkup: new InlineKeyboardMarkup(buttons)
        );
    }

    private async Task<bool> EnsureUserIsMember(long userId)
    {
        if (userId == AdminChatId) return true;
        if (_requiredChannels.Count == 0) return true;
        if (await IsUserMemberOfRequiredChannels(userId)) return true;
        await SendJoinChannelsPrompt(userId);
        return false;
    }

    public TelegramBotService(IConfiguration config, ILogger<TelegramBotService> logger, XuiService xuiService)
    {
        _config = config;
        _logger = logger;
        _requiredChannels = _config.GetSection("Telegram:RequiredChannels").Get<List<ChannelInfo>>() ?? [];
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
        var userId = message.From?.Id ?? message.Chat.Id;
        var isMember = await EnsureUserIsMember(userId);
        if (!isMember) return;

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
به بات پشتیبانی خوش اومدی ☺️❤️
امیدوارم تجربه‌ای راحت و سریع داشته باشی 🙌");
        }
        else if (message.Photo?.Any() == true)
        {
            await HandleReceipt(message);
        }
        else if (message.Text?.Contains("خرید کانفیگ") == true && message.Chat.Id != AdminChatId)
        {
            await SendPlanOptions(message.Chat.Id);
        }
        else if (message.Text?.Contains("پشتیبانی") == true && message.Chat.Id != AdminChatId)
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
        else if (message.Text?.Contains("دانلود نرم افزارها") == true && message.Chat.Id != AdminChatId)
        {

            var chatId = message.Chat.Id;

            var text =
                "📥 <b>دانلود نرم‌افزارهای اتصال</b>\n" +
                "لطفاً با توجه به دستگاه‌تون یکی از گزینه‌های زیر رو انتخاب کنید. لینک‌ها <b>رسمی</b> هستند.\n\n" +
                "🤖 اندروید   🖥️ ویندوز/لینوکس   🍎 آیفون/مک\n";

            var kb = new InlineKeyboardMarkup(new[]
            {
    // ANDROID (Google Play)
    new[]
    {
        InlineKeyboardButton.WithUrl("🤖 اندروید | NPV Tunnel", "https://play.google.com/store/apps/details?id=com.napsternetlabs.napsternetv"),
    },
    new[]
    {
        InlineKeyboardButton.WithUrl("🤖 اندروید | HiddifyNG", "https://play.google.com/store/apps/details?id=ang.hiddify.com"),
    },
    new[]
    {
        InlineKeyboardButton.WithUrl("🤖 اندروید | v2rayNG", "https://play.google.com/store/apps/details?id=dev.hexasoftware.v2box"),
    },

    // WINDOWS / LINUX (GitHub Releases)
    new[]
    {
        InlineKeyboardButton.WithUrl("🖥️ ویندوز/لینوکس | Hiddify (Releases)", "https://github.com/hiddify/hiddify-app/releases"),
    },
    new[]
    {
        InlineKeyboardButton.WithUrl("🖥️ ویندوز/لینوکس | Nekoray (Releases)", "https://github.com/Matsuridayo/nekoray/releases"),
    },
    new[]
    {
        InlineKeyboardButton.WithUrl("🖥️ ویندوز/لینوکس | v2rayN (Releases)", "https://github.com/2dust/v2rayN/releases"),
    },
    // (اختیاری برای لینوکس)
    new[]
    {
        InlineKeyboardButton.WithUrl("🐧 لینوکس | v2rayA (Releases)", "https://github.com/v2rayA/v2rayA/releases"),
    },

    // iOS (App Store)
    new[]
    {
        InlineKeyboardButton.WithUrl("🍎 مک/آیفون | NPV Tunnel", "https://apps.apple.com/app/npv-tunnel/id1629465476"),
    },
    new[]
    {
        InlineKeyboardButton.WithUrl("🍎 مک/آیفون | Hiddify", "https://apps.apple.com/app/hiddify-proxy-vpn/id6596777532"),
    },
    new[]
    {
        InlineKeyboardButton.WithUrl("🍎 مک/آیفون | V2Box (کلاینت V2Ray)", "https://apps.apple.com/app/v2box-v2ray-client/id6446814690"),
    },
});

            await _bot.SendMessage(chatId: chatId, text: text, parseMode: ParseMode.Html, replyMarkup: kb);



        }
        else if (message.Text?.Contains("علت:") == true && message.Chat.Id == AdminChatId)
        {
            var targetUserId = long.Parse(message.Text.Split(':')[1]);
            var reason = string.Join(':', message.Text.Split(':').Skip(2));
            await _bot.SendMessage(
                targetUserId,
                "⚠️ <b>کاربر گرامی</b>، رسید شما توسط همکاران ما در <b>نت‌کی</b> <b>رد شد</b>.\n\n" +
                $"📝 <b>علت رد:</b>\n{reason}\n\n" +
                "🔁 لطفاً پس از اصلاح، رسید صحیح را ارسال کنید یا در صورت تمایل طرح دیگری را انتخاب نمایید.\n\n" +
                "💻 پشتیبانی نت‌کی: \n@NetKeySupport\n\n" +
                "🆔 کد رهگیری: \n<code>" + targetUserId + "</code>",
                parseMode: ParseMode.Html
            );

            await _bot.SendMessage(
                AdminChatId,
                "⚠️ <b>همکار گرامی</b>\n" +
                "ردِ رسید ثبت شد و به کاربر اطلاع داده شد.\n\n" +
                $"📝 <b>علت رد:</b> {reason}\n\n" +
                "📌 لطفاً در دسترس باشید؛ احتمال دارد کاربر با <b>کد رهگیری</b> مراجعه کند.\n" +
                "ممنون از پیگیری‌تون 🙏\n\n" +
                "🆔 <code>" + targetUserId + "</code>",
                parseMode: ParseMode.Html
            );

        }
        else if (message.Text?.Contains("config:") == true && message.Chat.Id == AdminChatId)
        {
            var targetUserId = long.Parse(message.Text.Split(':')[1]);
            var configLink = string.Join(':', message.Text.Split(':').Skip(2));
            var qrBytes = GenerateQrCode(configLink);
            using var ms = new MemoryStream(qrBytes);
            await _bot.SendPhoto(
                chatId: targetUserId,
                photo: InputFile.FromStream(ms, "qr.png"),
                caption:
                    "🙏 <b>با تشکر از اعتماد شما</b>\n\n" +
                    "📸 برای اتصال، <b>QR</b> را اسکن کنید یا از لینک زیر استفاده کنید:\n" +
                    "<code>" + configLink + "</code>\n\n" +
                    "🤝 در صورت داشتن <b>مشکل</b> یا <b>درخواست</b>، با همکاران ما در بخش <b>پشتیبانی</b> در ارتباط باشید:\n" +
                    "<a href=\"https://t.me/NetKeySupport\">@NetKeySupport</a>",
                parseMode: ParseMode.Html
            );

            await _bot.SendMessage(
                AdminChatId,
                "✅ <b>تسک انجام شد</b>!\n\nلینک کاربر ارسال و تحویل شد. دمت گرم ✌️❤️\nاگه موردی بود خبر بده.",
                parseMode: ParseMode.Html
            );


        }
    }

    private byte[] GenerateQrCode(string link)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        var qrBytes = qrCode.GetGraphic(15);
        return qrBytes;
    }

    private async Task SendPlanOptions(long chatId)
    {
        var plans = _config.GetSection(ACTIVE_PLAN).Get<List<Plan>>() ?? new();
        // var plans = _config.GetSection("Plans").Get<List<Plan>>() ?? new();
        var buttons = plans.Select(p => new[] { InlineKeyboardButton.WithCallbackData($"{p.Name} - {p.Price} هزار تومان", $"plan:{p.Id}") });
        await _bot.SendMessage(chatId, @"همراه گرامی نت کی 🌹
تعرفه‌های نت‌کی خدمتتون ارسال شد.
لطفاً یکی از طرح‌ها رو انتخاب بفرمایید تا همکاران ما در نت کی در سریع‌ترین زمان فعال‌سازی طرح شما رو انجام بدن.",
replyMarkup: new InlineKeyboardMarkup(buttons));
        //         await _bot.SendMessage(chatId, @"به جشنواره فروش نت کی خوش اومدید 🎉
        // لطفاً یکی از طرح‌ها رو انتخاب بفرمایید تا همکاران ما در نت کی در سریع‌ترین زمان فعال‌سازی طرح شما رو انجام بدن.",
        //         replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task SendPlanOptionsAgain(long chatId)
    {
        var plans = _config.GetSection(ACTIVE_PLAN).Get<List<Plan>>() ?? new();
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

        if (query.Data != "joined" && !await EnsureUserIsMember(query.From.Id))
        {
            await _bot.AnswerCallbackQuery(query.Id);
            return;
        }

        if (query.Data == "joined")
        {
            if (await IsUserMemberOfRequiredChannels(query.From.Id))
            {
                await _bot.SendMessage(query.Message!.Chat.Id,
                    "✅ عضویت شما تأیید شد!",
                    replyMarkup: BuildMainKeyboard());
            }
            else
            {
                await SendJoinChannelsPrompt(query.Message!.Chat.Id);
            }
            await _bot.AnswerCallbackQuery(query.Id);
            return;
        }

        if (query.Data.StartsWith("plan:"))
        {
            var planId = query.Data.Split(':')[1];
            var plan = _config.GetSection(ACTIVE_PLAN).Get<List<Plan>>()?.FirstOrDefault(p => p.Id == planId);
            if (plan != null)
            {
                _userPlans[query.From.Id] = plan;
                await _bot.SendMessage(query.Message!.Chat.Id, $@"✅ طرح انتخابی شما: {plan.Description}.

💳 لطفاً مبلغ {plan.Price} هزار تومان را جهت تکمیل فرایند به کارت زیر واریز فرمایید و رسید را ارسال کنید:

{CardNumber}

⚠️ نکته مهم:
انتخاب طرح به معنی نهایی شدن آن نیست. شما می‌توانید هر تعداد بار طرح خود را تغییر دهید.
تا زمانی که رسید پرداخت ارسال نشود، آخرین طرح انتخابی شما به عنوان طرح فعال در نظر گرفته می‌شود.");
            }
        }
        else if (query.Data.StartsWith("approve:"))
        {
            var userId = long.Parse(query.Data.Split(':')[1]);

            // await _bot.SendMessage(userId,
            //     "✅ <b>کاربر گرامی</b>، رسید شما توسط همکاران ما در <b>نت‌کی</b> تأیید شد.\n\n" +
            //     "⏳ تا لحظاتی دیگر <b>کانفیگ</b> شما ارسال خواهد شد.\n\n" +
            //     "🆘 در صورت بروز هرگونه مشکل یا درخواست، با ارسال <b>کُد رهگیری</b> با همکاران ما در <b>نت‌کی</b> در ارتباط باشید.\n\n" +
            //     "💻 پشتیبانی نت‌کی: <code>@NetKeySupport</code>\n" +
            //     "🆔 کُد رهگیری: <code>" + userId + "</code>",
            //     parseMode: ParseMode.Html);

            var adminId = query.From.Id;
            await _bot.SendMessage(
                adminId,
                "✅ <b>همکار گرامی</b>\n" +
                "رسید توسط شما تأیید شد.\n\n" +
                "📌 لطفاً لینک کاربر را با فرمت زیر ارسال کنید:\n" +
                "config:[telegramId]:[configVless]\n\n" +
                "🆔 <code>" + userId + "</code>",
                parseMode: ParseMode.Html
            );

        }
        else if (query.Data.StartsWith("reject:"))
        {
            var userId = long.Parse(query.Data.Split(':')[1]);

            var adminId = query.From.Id;
            await _bot.SendMessage(
                adminId,
                "⚠️ <b>همکار گرامی</b>\n" +
                "رسیدی که <b>رد</b> کردید با موفقیت ثبت و به کاربر اطلاع داده شد.\n\n" +
                "📝 لطفاً <b>علت رد</b> را با فرمت زیر ارسال کنید:\n" +
                "<code>علت:[userId]:[دلیل رد رسید]</code>\n\n" +
                "📌 لطفاً در دسترس باشید؛ ممکن است کاربر با <b>کُد رهگیری</b> برای پیگیری به شما مراجعه کند.\n\n" +
                "🆔 <code>" + userId + "</code>",
                parseMode: ParseMode.Html
            );
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
            var caption = $@"🧾 درخواست بررسی رسید

👤 مشخصات کاربر
• شناسه تلگرام (کد رهگیری): {message.From.Id}
• نام کاربری: {message.From.Username ?? "ندارد"}

📦 طرح انتخابی
• {plan.Description}

📌 نکات مهم
1) مبلغ درج‌شده در رسید باید دقیقاً با مبلغ طرح یکسان باشد.
2) لینک Vless را بر اساس شناسه تلگرام کاربر تولید کنید: " +
"[<code>" + message.From.Id + "</code>]";

            using var stream = System.IO.File.OpenRead(path);
            var buttons = new InlineKeyboardMarkup(new[]
            {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("تایید", $"approve:{message.From.Id}"),
                InlineKeyboardButton.WithCallbackData("رد", $"reject:{message.From.Id}")
            }
        });
            await _bot.SendPhoto(AdminChatId,
                InputFile.FromStream(stream, Path.GetFileName(path)),
                caption: caption,
                parseMode: ParseMode.Html,
                replyMarkup: buttons);
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
