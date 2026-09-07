using System.Text.Json;
using AOI.PTH.RoiPrototype.Imaging;
using AOI.PTH.RoiPrototype.Models;
using AOI.PTH.RoiPrototype.Options;
using AOI.PTH.RoiPrototype.Vision;
using OpenCvSharp;

Console.OutputEncoding = System.Text.Encoding.UTF8;

try
{
    PrototypeOptions options = ParseArguments(args);
    Directory.CreateDirectory(options.OutputDirectory);

    using Mat cleanBoard = new FileImageSource(options.CleanImagePath).Load();
    using Mat assembledBoard = new FileImageSource(options.AssembledImagePath).Load();

    var alignmentService = new BoardAlignmentService();
    using AlignmentResult alignment = alignmentService.Align(cleanBoard, assembledBoard, options);

    var roiGenerator = new RoiGenerator();
    using RoiGenerationResult roiResult = roiGenerator.Generate(
        cleanBoard,
        alignment.AlignedImage,
        alignment.ValidAreaMask,
        options);
    using Mat overlay = roiGenerator.DrawRois(alignment.AlignedImage, roiResult.Rois);

    SaveImage(options.OutputDirectory, "01_montada_alinhada.png", alignment.AlignedImage);
    SaveImage(options.OutputDirectory, "02_diferenca.png", roiResult.DifferenceImage);
    SaveImage(options.OutputDirectory, "03_mascara_roi.png", roiResult.DifferenceMask);
    SaveImage(options.OutputDirectory, "04_rois_detectadas.png", overlay);

    var report = new
    {
        schemaVersion = 1,
        generatedAtUtc = DateTimeOffset.UtcNow,
        source = new
        {
            cleanImage = Path.GetFullPath(options.CleanImagePath),
            assembledImage = Path.GetFullPath(options.AssembledImagePath),
            width = cleanBoard.Width,
            height = cleanBoard.Height
        },
        alignment = new
        {
            status = "OK",
            alignment.CandidateMatches,
            alignment.InlierMatches,
            alignment.InlierRatio,
            alignment.MeanReprojectionErrorPixels,
            homography = MatToArray(alignment.Homography)
        },
        parameters = new
        {
            options.DifferenceThreshold,
            options.MinimumRoiArea,
            options.MaximumRoiAreaFraction,
            options.RoiPadding,
            options.MergeDistance
        },
        roiCount = roiResult.Rois.Count,
        rois = roiResult.Rois
    };

    string jsonPath = Path.Combine(options.OutputDirectory, "rois.json");
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine("Processamento concluído com segurança.");
    Console.WriteLine(
        $"Alinhamento: {alignment.InlierMatches}/{alignment.CandidateMatches} inliers " +
        $"({alignment.InlierRatio:P1}), erro médio {alignment.MeanReprojectionErrorPixels:F2}px.");
    Console.WriteLine($"ROIs candidatas: {roiResult.Rois.Count}");
    Console.WriteLine($"Resultados: {Path.GetFullPath(options.OutputDirectory)}");
    return 0;
}
catch (BoardAlignmentException exception)
{
    Console.Error.WriteLine($"ERROR - {exception.Message}");
    Console.Error.WriteLine("Nenhuma ROI foi aceita. Verifique enquadramento, foco e iluminação das duas fotos.");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"ERROR - {exception.Message}");
    return 1;
}

static PrototypeOptions ParseArguments(string[] arguments)
{
    if (arguments.Length < 2 || arguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine(
            "Uso:\n" +
            "  dotnet run -- <placa_limpa> <placa_montada> [pasta_saida] [opções]\n\n" +
            "Opções:\n" +
            "  --threshold <0..255>       Diferença mínima por pixel (padrão: 32)\n" +
            "  --min-area <pixels>        Área mínima de ROI (padrão: 250)\n" +
            "  --padding <pixels>         Margem ao redor de cada ROI (padrão: 10)\n" +
            "  --merge-distance <pixels>  Une regiões próximas (padrão: 16)");
        Environment.Exit(0);
    }

    string cleanPath = arguments[0];
    string assembledPath = arguments[1];
    int optionStart = 2;
    string outputDirectory = "resultado";
    if (arguments.Length > 2 && !arguments[2].StartsWith("--", StringComparison.Ordinal))
    {
        outputDirectory = arguments[2];
        optionStart = 3;
    }

    int threshold = 32;
    double minimumArea = 250;
    int padding = 10;
    int mergeDistance = 16;

    for (int index = optionStart; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length)
            throw new ArgumentException($"Valor ausente para a opção {arguments[index]}.");

        string value = arguments[index + 1];
        switch (arguments[index].ToLowerInvariant())
        {
            case "--threshold":
                threshold = int.Parse(value);
                break;
            case "--min-area":
                minimumArea = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "--padding":
                padding = int.Parse(value);
                break;
            case "--merge-distance":
                mergeDistance = int.Parse(value);
                break;
            default:
                throw new ArgumentException($"Opção desconhecida: {arguments[index]}");
        }
    }

    if (threshold is < 0 or > 255)
        throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold deve estar entre 0 e 255.");
    if (minimumArea <= 0 || padding < 0 || mergeDistance < 0)
        throw new ArgumentException("Área mínima deve ser positiva; padding e distância não podem ser negativos.");

    return new PrototypeOptions(
        cleanPath,
        assembledPath,
        outputDirectory,
        threshold,
        minimumArea,
        RoiPadding: padding,
        MergeDistance: mergeDistance);
}

static void SaveImage(string outputDirectory, string fileName, Mat image)
{
    string path = Path.Combine(outputDirectory, fileName);
    if (!Cv2.ImWrite(path, image))
        throw new IOException($"Falha ao salvar {Path.GetFullPath(path)}.");
}

static double[][] MatToArray(Mat matrix)
{
    var result = new double[matrix.Rows][];
    for (int row = 0; row < matrix.Rows; row++)
    {
        result[row] = new double[matrix.Cols];
        for (int column = 0; column < matrix.Cols; column++)
            result[row][column] = matrix.At<double>(row, column);
    }

    return result;
}
