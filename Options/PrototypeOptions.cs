namespace AOI.PTH.RoiPrototype.Options;

public sealed record PrototypeOptions(
    string CleanImagePath,
    string AssembledImagePath,
    string OutputDirectory,
    int DifferenceThreshold = 32,
    double MinimumRoiArea = 250,
    double MaximumRoiAreaFraction = 0.12,
    int RoiPadding = 10,
    int MergeDistance = 16,
    int MinimumMatches = 18,
    int MinimumInliers = 12,
    double MinimumInlierRatio = 0.35,
    double MaximumMeanReprojectionErrorPixels = 4.0);
