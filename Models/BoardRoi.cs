using OpenCvSharp;

namespace AOI.PTH.RoiPrototype.Models;

public sealed record BoardRoi(
    int Id,
    int X,
    int Y,
    int Width,
    int Height,
    double AreaPixels,
    double XNormalized,
    double YNormalized,
    double WidthNormalized,
    double HeightNormalized)
{
    public Rect ToRect() => new(X, Y, Width, Height);

    public static BoardRoi FromRect(int id, Rect rect, Size imageSize) => new(
        id,
        rect.X,
        rect.Y,
        rect.Width,
        rect.Height,
        rect.Width * rect.Height,
        rect.X / (double)imageSize.Width,
        rect.Y / (double)imageSize.Height,
        rect.Width / (double)imageSize.Width,
        rect.Height / (double)imageSize.Height);
}
