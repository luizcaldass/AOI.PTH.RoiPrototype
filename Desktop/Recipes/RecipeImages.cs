using System.IO;
using System.Windows.Media.Imaging;
using AOI.PTH.RoiPrototype.Options;
using AOI.PTH.RoiPrototype.Vision;
using OpenCvSharp;

namespace AOI.PTH.Desktop.Recipes;

public static class RecipeImages
{
    public const long MaxBytes = 64L * 1024 * 1024;
    public static byte[] ReadPhoto(string path)
    {
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Imagem maior que 64 MB.");
        var bytes = File.ReadAllBytes(path);
        Dimensions(bytes);
        using var decoded = Cv2.ImDecode(bytes, ImreadModes.Color | ImreadModes.IgnoreOrientation);
        if (decoded.Empty()) throw new InvalidDataException("Não foi possível decodificar a imagem.");
        return Png(decoded);
    }
    public static (int Width, int Height) Dimensions(byte[] data)
    {
        if (data.Length == 0 || data.LongLength > MaxBytes) throw new InvalidDataException("Imagem vazia ou maior que 64 MB.");
        using var stream = new MemoryStream(data, false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        if (decoder is not PngBitmapDecoder && decoder is not JpegBitmapDecoder)
            throw new InvalidDataException("Use imagens PNG ou JPEG.");
        var f = decoder.Frames[0];
        if (f.PixelWidth < 64 || f.PixelHeight < 64 || f.PixelWidth > 10000 || f.PixelHeight > 10000 || (long)f.PixelWidth * f.PixelHeight > 20_000_000)
            throw new InvalidDataException("Use imagens entre 64 px e 20 megapixels, com até 10.000 px por lado.");
        return (f.PixelWidth, f.PixelHeight);
    }
    public static BitmapSource Bitmap(byte[] bytes)
    {
        Dimensions(bytes);
        using var stream = new MemoryStream(bytes, false);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }
    public static byte[] Png(Mat mat)
    {
        Cv2.ImEncode(".png", mat, out byte[] bytes);
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("Imagem PNG excede 64 MB.");
        return bytes;
    }
    public static RecipeFiles Generate(byte[] clean, byte[] assembled, GenerationSettings settings)
    {
        ValidateSettings(settings);
        Dimensions(clean); Dimensions(assembled);
        using var c = Cv2.ImDecode(clean, ImreadModes.Color);
        using var a = Cv2.ImDecode(assembled, ImreadModes.Color);
        var options = new PrototypeOptions("", "", "", settings.Threshold, settings.MinArea, RoiPadding: settings.Padding, MergeDistance: settings.MergeDistance);
        using var alignment = new BoardAlignmentService().Align(c, a, options);
        double coverage = Cv2.CountNonZero(alignment.ValidAreaMask) / (double)(c.Width * c.Height);
        if (coverage < 0.75) throw new InvalidDataException("Sobreposição menor que 75%. Refotografe as placas com enquadramento semelhante.");
        var h = new double[9];
        for (int r = 0; r < 3; r++) for (int col = 0; col < 3; col++) h[r * 3 + col] = alignment.Homography.At<double>(r, col);
        if (h.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Transformação de alinhamento inválida.");
        using var result = new RoiGenerator().Generate(c, alignment.AlignedImage, alignment.ValidAreaMask, options);
        var document = new RecipeDocument
        {
            Width = c.Width, Height = c.Height, Generation = settings,
            Alignment = new(alignment.InlierMatches, alignment.CandidateMatches, alignment.MeanReprojectionErrorPixels, coverage, h),
            Rois = result.Rois.Select(r => new RecipeRoi { Reference = $"ROI{r.Id}", X = r.X, Y = r.Y, Width = r.Width, Height = r.Height }).ToList()
        };
        return new(document, clean, Png(alignment.AlignedImage), assembled, Png(alignment.ValidAreaMask));
    }
    public static void ValidateSettings(GenerationSettings p)
    {
        if (p.Threshold is < 1 or > 254 || p.MinArea is < 1 or > 20_000_000 || p.Padding is < 0 or > 500 || p.MergeDistance is < 0 or > 500)
            throw new InvalidDataException("Threshold: 1–254; área mínima: 1–20.000.000; margem e união: 0–500 px.");
    }
}
