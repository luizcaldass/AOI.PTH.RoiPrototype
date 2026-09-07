using AOI.PTH.RoiPrototype.Models;
using AOI.PTH.RoiPrototype.Options;
using OpenCvSharp;

namespace AOI.PTH.RoiPrototype.Vision;

public sealed class BoardAlignmentService
{
    private const int MaximumFeatures = 5000;
    private const double LoweRatio = 0.75;
    private const double RansacThresholdPixels = 3.0;
    private const int ValidMaskErosionPixels = 5;

    public AlignmentResult Align(Mat referenceCleanBoard, Mat movingAssembledBoard, PrototypeOptions options)
    {
        ValidateInput(referenceCleanBoard, movingAssembledBoard);

        using Mat referenceGray = ToGray(referenceCleanBoard);
        using Mat movingGray = ToGray(movingAssembledBoard);
        using var orb = ORB.Create(MaximumFeatures);
        using var referenceDescriptors = new Mat();
        using var movingDescriptors = new Mat();

        orb.DetectAndCompute(referenceGray, null, out KeyPoint[] referenceKeypoints, referenceDescriptors);
        orb.DetectAndCompute(movingGray, null, out KeyPoint[] movingKeypoints, movingDescriptors);

        if (referenceDescriptors.Empty() || movingDescriptors.Empty())
            throw new BoardAlignmentException("Não há detalhes visuais suficientes para alinhar as placas.");

        using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false);
        DMatch[][] neighborMatches = matcher.KnnMatch(movingDescriptors, referenceDescriptors, k: 2);
        DMatch[] goodMatches = neighborMatches
            .Where(matches => matches.Length == 2 && matches[0].Distance < LoweRatio * matches[1].Distance)
            .Select(matches => matches[0])
            .ToArray();

        if (goodMatches.Length < options.MinimumMatches)
        {
            throw new BoardAlignmentException(
                $"Alinhamento inseguro: apenas {goodMatches.Length} correspondências confiáveis; " +
                $"mínimo configurado = {options.MinimumMatches}.");
        }

        Point2d[] movingPoints = goodMatches
            .Select(match => new Point2d(
                movingKeypoints[match.QueryIdx].Pt.X,
                movingKeypoints[match.QueryIdx].Pt.Y))
            .ToArray();
        Point2d[] referencePoints = goodMatches
            .Select(match => new Point2d(
                referenceKeypoints[match.TrainIdx].Pt.X,
                referenceKeypoints[match.TrainIdx].Pt.Y))
            .ToArray();

        using Mat inlierMask = new();
        Mat homography = Cv2.FindHomography(
            movingPoints,
            referencePoints,
            HomographyMethods.Ransac,
            RansacThresholdPixels,
            inlierMask);

        if (homography.Empty())
        {
            homography.Dispose();
            throw new BoardAlignmentException("Não foi possível calcular a transformação de perspectiva.");
        }

        int inliers = Cv2.CountNonZero(inlierMask);
        double inlierRatio = inliers / (double)goodMatches.Length;
        double meanError = CalculateMeanReprojectionError(movingPoints, referencePoints, homography, inlierMask);

        if (inliers < options.MinimumInliers ||
            inlierRatio < options.MinimumInlierRatio ||
            meanError > options.MaximumMeanReprojectionErrorPixels)
        {
            homography.Dispose();
            throw new BoardAlignmentException(
                "Alinhamento rejeitado pela validação: " +
                $"inliers={inliers}/{goodMatches.Length} ({inlierRatio:P1}), " +
                $"erro médio={meanError:F2}px.");
        }

        var aligned = new Mat();
        Cv2.WarpPerspective(
            movingAssembledBoard,
            aligned,
            homography,
            referenceCleanBoard.Size(),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.Black);

        using var sourceMask = new Mat(movingAssembledBoard.Size(), MatType.CV_8UC1, Scalar.White);
        var validAreaMask = new Mat();
        Cv2.WarpPerspective(
            sourceMask,
            validAreaMask,
            homography,
            referenceCleanBoard.Size(),
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.Black);

        using Mat erosionKernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(ValidMaskErosionPixels * 2 + 1, ValidMaskErosionPixels * 2 + 1));
        Cv2.Erode(validAreaMask, validAreaMask, erosionKernel);

        return new AlignmentResult(
            aligned,
            validAreaMask,
            homography,
            goodMatches.Length,
            inliers,
            inlierRatio,
            meanError);
    }

    private static Mat ToGray(Mat image)
    {
        var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(3, 3), 0);
        return gray;
    }

    private static double CalculateMeanReprojectionError(
        Point2d[] movingPoints,
        Point2d[] referencePoints,
        Mat homography,
        Mat inlierMask)
    {
        Point2d[] projected = Cv2.PerspectiveTransform(movingPoints, homography);
        double totalError = 0;
        int count = 0;

        for (int index = 0; index < projected.Length; index++)
        {
            if (inlierMask.At<byte>(index, 0) == 0)
                continue;

            double deltaX = projected[index].X - referencePoints[index].X;
            double deltaY = projected[index].Y - referencePoints[index].Y;
            totalError += Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            count++;
        }

        return count == 0 ? double.PositiveInfinity : totalError / count;
    }

    private static void ValidateInput(Mat reference, Mat moving)
    {
        if (reference.Empty())
            throw new ArgumentException("A imagem da placa limpa é inválida.", nameof(reference));
        if (moving.Empty())
            throw new ArgumentException("A imagem da placa montada é inválida.", nameof(moving));
        if (reference.Channels() != 3 || moving.Channels() != 3)
            throw new ArgumentException("As imagens devem ser coloridas (BGR/RGB). ");
    }
}
