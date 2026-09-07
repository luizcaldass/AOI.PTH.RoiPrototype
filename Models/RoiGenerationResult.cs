using OpenCvSharp;

namespace AOI.PTH.RoiPrototype.Models;

public sealed record RoiGenerationResult(
    IReadOnlyList<BoardRoi> Rois,
    Mat DifferenceImage,
    Mat DifferenceMask) : IDisposable
{
    public void Dispose()
    {
        DifferenceImage.Dispose();
        DifferenceMask.Dispose();
    }
}
