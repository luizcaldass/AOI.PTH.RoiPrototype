using OpenCvSharp;

namespace AOI.PTH.RoiPrototype.Imaging;

public sealed class FileImageSource(string path) : IImageSource
{
    public Mat Load()
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Imagem não encontrada.", Path.GetFullPath(path));

        Mat image = Cv2.ImRead(path, ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidDataException($"Não foi possível decodificar a imagem: {Path.GetFullPath(path)}");
        }

        return image;
    }
}
