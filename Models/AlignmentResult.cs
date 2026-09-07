using OpenCvSharp;

namespace AOI.PTH.RoiPrototype.Models;

public sealed record AlignmentResult(
    Mat AlignedImage,
    Mat ValidAreaMask,
    Mat Homography,
    int CandidateMatches,
    int InlierMatches,
    double InlierRatio,
    double MeanReprojectionErrorPixels) : IDisposable
{
    public void Dispose()
    {
        AlignedImage.Dispose();
        ValidAreaMask.Dispose();
        Homography.Dispose();
    }
}
