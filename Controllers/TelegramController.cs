using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using V2rayApi.Services;

namespace V2rayApi.Controllers;

[ApiController]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    private readonly TelegramBotService _botService;

    public TelegramController(TelegramBotService botService)
    {
        _botService = botService;
    }

    [HttpPost("update")]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        await _botService.HandleUpdateAsync(update);
        return Ok();
    }
}
