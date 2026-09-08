using AOI.PTH.Desktop.ViewModels;

namespace AOI.PTH.Desktop.Recipes;

public sealed class RecipeRoi : Observable
{
    private string reference = "ROI1";
    private int x, y, width = 40, height = 40;
    private bool enabled = true;
    public string Reference { get => reference; set { reference = value; Changed(); } }
    public int X { get => x; set { x = value; Changed(); } }
    public int Y { get => y; set { y = value; Changed(); } }
    public int Width { get => width; set { width = value; Changed(); } }
    public int Height { get => height; set { height = value; Changed(); } }
    public bool Enabled { get => enabled; set { enabled = value; Changed(); } }
}

public sealed record AlignmentInfo(int Inliers, int Matches, double ErrorPixels, double Coverage, double[] Homography);
public sealed record GenerationSettings(int Threshold = 32, int MinArea = 250, int Padding = 10, int MergeDistance = 16);
public sealed class RecipeDocument
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Version { get; set; } = 1;
    public string Model { get; set; } = "";
    public string BoardRevision { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Author { get; set; } = Environment.UserName;
    public string Source { get; set; } = "files";
    public string CoordinateSystem { get; set; } = "clean-reference-pixels";
    public string Status { get; set; } = "OfflineTest";
    public bool RoiReviewConfirmed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public AlignmentInfo? Alignment { get; set; }
    public GenerationSettings Generation { get; set; } = new();
    public List<RecipeRoi> Rois { get; set; } = [];
}

public sealed record RecipeFiles(RecipeDocument Document, byte[] Clean, byte[] Assembled, byte[] OriginalAssembled, byte[] ValidMask);
public sealed record LocalRecipe(RecipeDocument Document, string Directory)
{
    public string Code => Document.Model;
    public string Revision => $"{Document.BoardRevision} / v{Document.Version}";
    public string Components => $"{Document.Rois.Count(r => r.Enabled)} ROIs ativas";
    public string State => "Teste offline • revisada";
    public string Display => $"{Code} · Rev. {Revision}";
}
