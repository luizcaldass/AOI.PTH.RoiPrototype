using OpenCvSharp;

namespace AOI.PTH.RoiPrototype.Imaging;

public interface IImageSource
{
    Mat Load();
}
