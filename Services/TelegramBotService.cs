using System.Collections.Concurrent;
using Telegram.Bot;
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
    private readonly string ACTIVE_PLAN = "Eco";
    // private readonly string ACTIVE_PLAN = "SpecialPlans";

    private long AdminChatId => long.Parse(_config["Telegram:AdminChatId"] ?? "0");
    private string CardNumber => _config["Payment:CardNumber"] ?? string.Empty;
    private string WalletAddress => _config["Payment:WalletAddress"] ?? string.Empty;
    private string[] RequiredChannels => _config.GetSection("Telegram:RequiredChannels").Get<string[]>() ?? Array.Empty<string>();

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

    public TelegramBotService(IConfiguration config, ILogger<TelegramBotService> logger, XuiService xuiService)
    {
        _config = config;
        _logger = logger;
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
        // Gate: force non-admins to be subscribed to all required channels
        if (message.Chat.Id != AdminChatId)
        {
            var ok = await EnsureMembershipOrPrompt(message.Chat.Id, message.From!.Id);
            if (!ok)
            {
                return; // Stop processing until user joins
            }
        }

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
        else if (message.Text.Contains("دانلود نرم افزارها") && message.Chat.Id != AdminChatId)
        {

            var userId = message.Chat.Id;

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

            await _bot.SendMessage(chatId: userId, text: text, parseMode: ParseMode.Html, replyMarkup: kb);



        }
        else if (message.Text.Contains("علت:") && message.Chat.Id == AdminChatId)
        {
            var userId = long.Parse(message.Text.Split(':')[1]);
            var reason = string.Join(':', message.Text.Split(':').Skip(2));
            await _bot.SendMessage(
                userId,
                "⚠️ <b>کاربر گرامی</b>، رسید شما توسط همکاران ما در <b>نت‌کی</b> <b>رد شد</b>.\n\n" +
                $"📝 <b>علت رد:</b>\n{reason}\n\n" +
                "🔁 لطفاً پس از اصلاح، رسید صحیح را ارسال کنید یا در صورت تمایل طرح دیگری را انتخاب نمایید.\n\n" +
                "💻 پشتیبانی نت‌کی: \n@NetKeySupport\n\n" +
                "🆔 کد رهگیری: \n<code>" + userId + "</code>",
                parseMode: ParseMode.Html
            );

            await _bot.SendMessage(
                AdminChatId,
                "⚠️ <b>همکار گرامی</b>\n" +
                "ردِ رسید ثبت شد و به کاربر اطلاع داده شد.\n\n" +
                $"📝 <b>علت رد:</b> {reason}\n\n" +
                "📌 لطفاً در دسترس باشید؛ احتمال دارد کاربر با <b>کد رهگیری</b> مراجعه کند.\n" +
                "ممنون از پیگیری‌تون 🙏\n\n" +
                "🆔 <code>" + userId + "</code>",
                parseMode: ParseMode.Html
            );

        }
        else if (message.Text.Contains("config:") && message.Chat.Id == AdminChatId)
        {
            var userId = long.Parse(message.Text.Split(':')[1]);
            var configLink = string.Join(':', message.Text.Split(':').Skip(2));
            var qrBytes = GenerateQrCode(configLink);
            using var ms = new MemoryStream(qrBytes);
            await _bot.SendPhoto(
                chatId: userId,
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

    private InlineKeyboardMarkup BuildJoinChannelsKeyboard()
    {
        var rows = new List<IEnumerable<InlineKeyboardButton>>();
        foreach (var ch in RequiredChannels)
        {
            var handle = ch.Trim();
            if (string.IsNullOrWhiteSpace(handle)) continue;
            var label = handle.StartsWith("@") ? handle : "@" + handle;
            var urlHandle = handle.TrimStart('@');
            rows.Add(new[] { InlineKeyboardButton.WithUrl($"عضویت در {label}", $"https://t.me/{urlHandle}") });
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("بررسی عضویت ✅", "check_sub") });
        return new InlineKeyboardMarkup(rows);
    }

    private async Task<bool> EnsureMembershipOrPrompt(long chatId, long userId)
    {
        // If no channels configured, allow.
        if (RequiredChannels.Length == 0) return true;

        if (await IsUserSubscribedToAll(userId))
            return true;

        var text = "🔒 برای استفاده از بات، لطفاً ابتدا در کانال‌های زیر عضو شوید و سپس روی دکمه ‘بررسی عضویت’ بزنید:";
        await _bot.SendMessage(chatId, text, replyMarkup: BuildJoinChannelsKeyboard());
        return false;
    }

    private async Task<bool> IsUserSubscribedToAll(long userId)
    {
        // Consider admin always subscribed
        if (userId == AdminChatId) return true;

        if (RequiredChannels.Length == 0) return true;

        foreach (var ch in RequiredChannels)
        {
            var handle = ch?.Trim();
            if (string.IsNullOrWhiteSpace(handle)) continue;
            try
            {
                var member = await _bot.GetChatMember(handle, userId);
                // Accept creator/administrator/member
                var status = member.Status;
                if (status != ChatMemberStatus.Creator &&
                    status != ChatMemberStatus.Administrator &&
                    status != ChatMemberStatus.Member)
                {
                    return false;
                }
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException apiEx)
            {
                // Common when channel hides members or bot isn't admin/member
                if (apiEx.ErrorCode == 400 && apiEx.Message.Contains("member list is inaccessible", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(apiEx, "Membership check inaccessible for user {UserId} in {Channel}. Bot must be admin/member in the channel.", userId, handle);
                    return false; // Cannot verify -> treat as not subscribed
                }
                _logger.LogWarning(apiEx, "Telegram API error on membership check for {UserId} in {Channel}", userId, handle);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Membership check failed for user {UserId} in {Channel}", userId, handle);
                return false;
            }
        }
        return true;
    }

    private async Task SendPlanOptions(long chatId)
    {
        var plans = _config.GetSection(ACTIVE_PLAN).Get<List<Plan>>() ?? new();
        // var plans = _config.GetSection("Plans").Get<List<Plan>>() ?? new();
        var buttons = plans.Select(p => new[] { InlineKeyboardButton.WithCallbackData($"{p.Name} - {p.Price} هزار تومان", $"plan:{p.Id}") });
        await _bot.SendMessage(chatId, @"📦 همه‌ی پلن‌ها یک‌ماهه هستن.
🖥 روی همه سیستم‌عامل‌ها و دیوایس ها بدون محدودیت کار می‌کنن (اندروید، آیفون، ویندوز، مک، لینوکس).

🌹 تعرفه‌ها همین الان خدمتتون ارسال شد.
کافیه پلن مناسب خودتون رو انتخاب کنید تا همکاران ما سریع براتون فعال‌سازی کنن.

از منوی زیر گزینه دلخواهتون رو انتخاب کنید👇🏻",
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
        // Allow pressing the re-check button without prior membership
        if (query.From.Id != AdminChatId && query.Data != "check_sub")
        {
            var ok = await IsUserSubscribedToAll(query.From.Id);
            if (!ok)
            {
                var newText = "هنوز عضو همه کانال‌ها نشدی. بعد از عضویت روی ‘بررسی عضویت ✅’ بزن.";
                try
                {
                    if (query.Message!.Text == newText)
                    {
                        await _bot.AnswerCallbackQuery(query.Id, "اول عضو شو بعد دوباره بررسی کن.", showAlert: false);
                    }
                    else
                    {
                        await _bot.EditMessageText(
                            chatId: query.Message.Chat.Id,
                            messageId: query.Message.MessageId,
                            text: newText,
                            replyMarkup: BuildJoinChannelsKeyboard()
                        );
                    }
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException e) when (e.ErrorCode == 400 && e.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                {
                    // Ignore harmless error
                }
                await _bot.AnswerCallbackQuery(query.Id);
                return;
            }
        }

        if (query.Data == "check_sub")
        {
            var ok = await IsUserSubscribedToAll(query.From.Id);
            if (ok)
            {
                var successText = "✅ عضویت شما تایید شد. از منوی زیر استفاده کنید.";
                try
                {
                    if (query.Message!.Text != successText)
                    {
                        await _bot.EditMessageText(
                            chatId: query.Message.Chat.Id,
                            messageId: query.Message.MessageId,
                            text: successText,
                            replyMarkup: null
                        );
                    }
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException e) when (e.ErrorCode == 400 && e.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                {
                    // ignore
                }
                await _bot.SendMessage(query.Message.Chat.Id, "از منوی زیر یکی از گزینه‌ها رو انتخاب کن:", replyMarkup: BuildMainKeyboard());
            }
            else
            {
                var failText = "❗️ هنوز عضو همه کانال‌ها نیستی. لطفاً عضو شو و دوباره امتحان کن.";
                try
                {
                    if (query.Message!.Text == failText)
                    {
                        await _bot.AnswerCallbackQuery(query.Id, "عضویت کامل نیست. حتما هر دو کانال را عضو شوید.", showAlert: false);
                    }
                    else
                    {
                        await _bot.EditMessageText(
                            chatId: query.Message.Chat.Id,
                            messageId: query.Message.MessageId,
                            text: failText,
                            replyMarkup: BuildJoinChannelsKeyboard()
                        );
                    }
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException e) when (e.ErrorCode == 400 && e.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                {
                    // ignore
                }
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
        // Membership gate already checked at HandleMessage.
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
