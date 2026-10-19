using System.IO;
using System.Windows.Media.Imaging;
using PDFtoImage;
using SkiaSharp;

namespace Semojum.Desktop.Services;

public static class PdfPreview
{
    private static readonly SemaphoreSlim rendering = new(1, 1);

    public static async Task<BitmapSource> RenderAsync(byte[] bytes, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        await rendering.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // CPU rendering avoids Windows.Data.Pdf's D3D/WARP teardown failure. Keep all
            // native images scoped to this operation; only an independent frozen bitmap escapes.
            return await Task.Run<BitmapSource>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var rendered = Conversion.ToImage(bytes, page: pageNumber - 1,
                    options: new RenderOptions(Width: 1800, WithAspectRatio: true, WithAnnotations: true));
                cancellationToken.ThrowIfCancellationRequested();
                using var png = rendered.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = png.AsStream();
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                return bitmap;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (PDFtoImage.Exceptions.PdfPasswordProtectedException ex)
        { throw new InvalidDataException("암호가 설정된 PDF는 원본 미리보기를 열 수 없습니다.", ex); }
        catch (PDFtoImage.Exceptions.PdfException ex)
        { throw new InvalidDataException("PDF 파일 또는 요청한 쪽을 읽을 수 없습니다.", ex); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        { throw new InvalidDataException("PDF 표시 구성 요소를 불러오지 못했습니다. 배포 폴더 전체를 다시 설치해 주세요.", ex); }
        finally { rendering.Release(); }
    }
}
