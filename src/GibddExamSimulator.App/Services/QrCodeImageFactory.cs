using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace GibddExamSimulator.Services;

public static class QrCodeImageFactory
{
    public static BitmapImage Create(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("QR payload must not be empty.", nameof(payload));
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        var bytes = code.GetGraphic(12, drawQuietZones: true);
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
