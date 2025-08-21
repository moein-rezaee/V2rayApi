using QRCoder;
using V2rayApi.Models;

namespace V2rayApi.Services;

public class XuiService
{
    private readonly ILogger<XuiService> _logger;

    public XuiService(ILogger<XuiService> logger)
    {
        _logger = logger;
    }

    // Placeholder for calling 3x-ui API to create inbound and return config link
    public Task<(string Link, byte[] QrCode)> CreateInboundAsync(long userId, Plan plan)
    {
        // TODO: Integrate with real 3x-ui API
        var fakeLink = $"vless://{Guid.NewGuid()}@example.com:443?security=none#{plan.Name}";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(fakeLink, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        var qrBytes = qrCode.GetGraphic(20);
        return Task.FromResult((fakeLink, qrBytes));
    }
}
