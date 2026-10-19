using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Semojum.Desktop.Services;

public static class PdfPreview
{
    // Keep PDF COM objects off the WPF STA. Only the frozen bitmap crosses back to the UI.
    public static Task<BitmapSource> RenderAsync(byte[] bytes, int pageNumber, CancellationToken cancellationToken) =>
        Task.Run(() => RenderCoreAsync(bytes, pageNumber, cancellationToken), cancellationToken);

    private static async Task<BitmapSource> RenderCoreAsync(byte[] bytes, int pageNumber, CancellationToken cancellationToken)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(cancellationToken); }
        var document = await PdfDocument.LoadFromStreamAsync(input).AsTask(cancellationToken);
        if (pageNumber < 1 || pageNumber > document.PageCount) throw new InvalidDataException("PDF 원본에 해당 쪽이 없습니다.");
        using var page = document.GetPage((uint)(pageNumber - 1));
        using var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, new PdfPageRenderOptions { DestinationWidth = 1800 }).AsTask(cancellationToken);
        output.Seek(0);
        using var stream = output.AsStreamForRead();
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }
}
