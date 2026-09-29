using System.IO;
using AOI.PTH.RoiPrototype.Options;
using AOI.PTH.RoiPrototype.Vision;
using OpenCvSharp;

namespace AOI.PTH.Desktop.Recipes;

public sealed record RoiAssessment(string Reference, string Result, double Coverage, double CleanDifference, double AssembledDifference, string Reason);
public sealed record PhotoAssessment(byte[] Aligned, double ErrorPixels, IReadOnlyList<RoiAssessment> Regions);

public static class RecipeInspection
{
    public static PhotoAssessment Inspect(RecipeFiles recipe, byte[] photo)
    {
        RecipeStore.ValidateFiles(recipe);
        RecipeImages.Dimensions(photo);
        using var clean = Cv2.ImDecode(recipe.Clean, ImreadModes.Color);
        using var assembled = Cv2.ImDecode(recipe.Assembled, ImreadModes.Color);
        using var test = Cv2.ImDecode(photo, ImreadModes.Color);
        using var alignment = new BoardAlignmentService().Align(assembled, test, new PrototypeOptions("", "", ""));
        if (Cv2.CountNonZero(alignment.ValidAreaMask) / (double)(clean.Width * clean.Height) < .75)
            throw new InvalidDataException("A foto cobre menos de 75% da referência. Verifique o enquadramento.");
        using var valid = Cv2.ImDecode(recipe.ValidMask, ImreadModes.Grayscale);
        Cv2.BitwiseAnd(valid, alignment.ValidAreaMask, valid);
        var regions = recipe.Document.Rois.Where(r => r.Enabled).Select(roi => Evaluate(clean, assembled, alignment.AlignedImage, valid, roi)).ToArray();
        return new(RecipeImages.Png(alignment.AlignedImage), alignment.MeanReprojectionErrorPixels, regions);
    }
    // Offline similarity heuristic, not calibrated probability or a production acceptance decision.
    public static RoiAssessment Evaluate(Mat clean, Mat assembled, Mat test, Mat valid, RecipeRoi roi)
    {
        var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height);
        using var mask = new Mat(valid, rect);
        double coverage = Cv2.CountNonZero(mask) / (double)(roi.Width * roi.Height);
        if (coverage < roi.MinimumCoverage) return new(roi.Reference, "REVISÃO MANUAL", coverage, 0, 0, "ROI parcialmente fora da área válida.");
        using var c = GrayCrop(clean, rect); using var a = GrayCrop(assembled, rect); using var t = GrayCrop(test, rect);
        double contrast = Difference(c, a, mask);
        double emptyDistance = Difference(t, c, mask), fullDistance = Difference(t, a, mask);
        if (contrast < roi.MinimumReferenceDifference)
            return new(roi.Reference, "REVISÃO MANUAL", coverage, emptyDistance, fullDistance, "As referências não distinguem suficientemente o componente.");
        double separation = (emptyDistance - fullDistance) / Math.Max(emptyDistance + fullDistance, 1);
        if (Math.Min(emptyDistance, fullDistance) > roi.MaximumMatchDifference)
            return new(roi.Reference, "REVISÃO MANUAL", coverage, emptyDistance, fullDistance, "A aparência diverge das duas referências.");
        string result = separation >= roi.DecisionMargin ? "PRESENTE" : separation <= -roi.DecisionMargin ? "AUSENTE" : "REVISÃO MANUAL";
        return new(roi.Reference, result, coverage, emptyDistance, fullDistance,
            result == "REVISÃO MANUAL" ? "Sem separação suficiente entre as referências." : "Semelhança visual offline; requer validação com fotos reais.");
    }
    private static Mat GrayCrop(Mat image, Rect rect)
    {
        using var crop = new Mat(image, rect); var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY); Cv2.GaussianBlur(gray, gray, new Size(3, 3), 0); return gray;
    }
    private static double Difference(Mat a, Mat b, Mat mask) { using var difference = new Mat(); Cv2.Absdiff(a, b, difference); return Cv2.Mean(difference, mask).Val0; }
}
