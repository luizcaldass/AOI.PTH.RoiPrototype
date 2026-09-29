using AOI.PTH.RoiPrototype.Models;
using AOI.PTH.RoiPrototype.Options;
using OpenCvSharp;

namespace AOI.PTH.RoiPrototype.Vision;

public sealed class RoiGenerator
{
    public RoiGenerationResult Generate(
        Mat cleanBoard,
        Mat alignedAssembledBoard,
        Mat validAreaMask,
        PrototypeOptions options)
    {
        if (cleanBoard.Size() != alignedAssembledBoard.Size())
            throw new ArgumentException("As imagens precisam ter a mesma resolução após o alinhamento.");
        if (validAreaMask.Size() != cleanBoard.Size())
            throw new ArgumentException("A máscara de área válida possui resolução incorreta.");

        using Mat cleanGray = NormalizeForComparison(cleanBoard);
        using Mat assembledGray = NormalizeForComparison(alignedAssembledBoard);
        var difference = new Mat();
        Cv2.Absdiff(cleanGray, assembledGray, difference);

        var mask = new Mat();
        Cv2.Threshold(difference, mask, options.DifferenceThreshold, 255, ThresholdTypes.Binary);

        using Mat openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 9));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, openKernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel, iterations: 2);
        Cv2.BitwiseAnd(mask, validAreaMask, mask);

        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        double maximumArea = cleanBoard.Width * cleanBoard.Height * options.MaximumRoiAreaFraction;
        Rect imageBounds = new(0, 0, cleanBoard.Width, cleanBoard.Height);
        List<Rect> candidates = contours
            .Where(contour => Cv2.ContourArea(contour) >= options.MinimumRoiArea)
            .Select(Cv2.BoundingRect)
            .Where(rect => rect.Width * rect.Height <= maximumArea)
            .Select(rect => AddPadding(rect, options.RoiPadding, imageBounds))
            .ToList();

        List<Rect> merged = MergeNearby(candidates, options.MergeDistance, imageBounds)
            .Where(rect => (long)rect.Width * rect.Height <= maximumArea)
            .OrderBy(rect => rect.Y)
            .ThenBy(rect => rect.X)
            .ToList();

        IReadOnlyList<BoardRoi> rois = merged
            .Select((rect, index) => BoardRoi.FromRect(index + 1, rect, cleanBoard.Size()))
            .ToArray();

        return new RoiGenerationResult(rois, difference, mask);
    }

    public Mat DrawRois(Mat image, IReadOnlyList<BoardRoi> rois)
    {
        Mat result = image.Clone();
        foreach (BoardRoi roi in rois)
        {
            Rect rect = roi.ToRect();
            Cv2.Rectangle(result, rect, new Scalar(0, 0, 255), 3);
            Point labelOrigin = new(rect.X, Math.Max(24, rect.Y - 7));
            Cv2.PutText(
                result,
                $"ROI {roi.Id}",
                labelOrigin,
                HersheyFonts.HersheySimplex,
                0.7,
                new Scalar(0, 0, 255),
                2,
                LineTypes.AntiAlias);
        }

        return result;
    }

    private static Mat NormalizeForComparison(Mat source)
    {
        var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);

        var normalized = new Mat();
        Cv2.Normalize(gray, normalized, 0, 255, NormTypes.MinMax);
        gray.Dispose();
        return normalized;
    }

    private static Rect AddPadding(Rect rect, int padding, Rect bounds)
    {
        int left = Math.Max(bounds.Left, rect.Left - padding);
        int top = Math.Max(bounds.Top, rect.Top - padding);
        int right = Math.Min(bounds.Right, rect.Right + padding);
        int bottom = Math.Min(bounds.Bottom, rect.Bottom + padding);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static IReadOnlyList<Rect> MergeNearby(List<Rect> rectangles, int distance, Rect bounds)
    {
        var pending = new List<Rect>(rectangles);
        var merged = new List<Rect>();

        while (pending.Count > 0)
        {
            Rect current = pending[0];
            pending.RemoveAt(0);

            bool changed;
            do
            {
                changed = false;
                Rect expanded = AddPadding(current, distance, bounds);
                for (int index = pending.Count - 1; index >= 0; index--)
                {
                    if (!Intersects(expanded, pending[index]))
                        continue;

                    current = Union(current, pending[index]);
                    pending.RemoveAt(index);
                    changed = true;
                }
            }
            while (changed);

            merged.Add(current);
        }

        return merged;
    }

    private static bool Intersects(Rect left, Rect right) =>
        left.Left < right.Right && left.Right > right.Left &&
        left.Top < right.Bottom && left.Bottom > right.Top;

    private static Rect Union(Rect left, Rect right)
    {
        int x1 = Math.Min(left.Left, right.Left);
        int y1 = Math.Min(left.Top, right.Top);
        int x2 = Math.Max(left.Right, right.Right);
        int y2 = Math.Max(left.Bottom, right.Bottom);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }
}
