using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AOI.PTH.Desktop.Recipes;

public sealed class RecipeStore(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AOI.PTH", "receitas");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    public static readonly string[] PayloadNames = ["receita.json", "imagens/placa_limpa.png", "imagens/placa_montada.png", "imagens/montada_original.png", "imagens/area_valida.png"];
    private const string IntegrityName = "integridade.json";
    private const long MaxTotal = 180L * 1024 * 1024;

    public IReadOnlyList<LocalRecipe> List(out string warnings)
    {
        var list = new List<LocalRecipe>();
        var invalid = 0;
        if (Directory.Exists(Root)) foreach (string directory in Directory.EnumerateDirectories(Root).Where(d => !Path.GetFileName(d).StartsWith('.')))
        {
            try
            {
                // Lightweight catalog listing. Full hashes/images are checked on activation/export.
                var document = Deserialize<RecipeDocument>(ReadBounded(Path.Combine(directory, "receita.json"), 1024 * 1024));
                ValidateDocument(document);
                if (Path.GetFileName(directory) != Key(document)) throw new InvalidDataException("Identificação da pasta inconsistente.");
                list.Add(new(document, directory));
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { invalid++; }
        }
        warnings = invalid == 0 ? "" : $"{invalid} pasta(s) inválida(s) ignorada(s); nenhum arquivo foi apagado.";
        return list.OrderBy(x => x.Code).ThenByDescending(x => x.Document.Version).ToArray();
    }
    public LocalRecipe Save(RecipeFiles files)
    {
        ValidateFiles(files);
        var payload = MakePayload(files);
        return Install(payload);
    }
    public LocalRecipe Import(string package)
    {
        if (!string.Equals(Path.GetExtension(package), ".aoireceita", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Selecione um arquivo .aoireceita.");
        using var stream = File.OpenRead(package);
        if (stream.Length > MaxTotal) throw new InvalidDataException("Pacote maior que 180 MB.");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var allowed = PayloadNames.Append(IntegrityName).ToHashSet(StringComparer.Ordinal);
        if (zip.Entries.Count != allowed.Count) throw new InvalidDataException("Quantidade de arquivos incorreta no pacote.");
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            // Strict names: no relative paths, drive names, alternate streams, duplicates or nested ZIPs.
            if (!allowed.Contains(entry.FullName) || payload.ContainsKey(entry.FullName)) throw new InvalidDataException("Arquivo inesperado ou duplicado no pacote.");
            long limit = Limit(entry.FullName);
            total += entry.Length;
            if (entry.Length <= 0 || entry.Length > limit || total > MaxTotal) throw new InvalidDataException("Limite de tamanho do pacote excedido.");
            using var source = entry.Open();
            payload.Add(entry.FullName, ReadBounded(source, limit));
        }
        VerifyPayload(payload);
        return Install(payload);
    }
    public RecipeFiles Load(LocalRecipe item)
    {
        var expected = Path.Combine(Root, Key(item.Document));
        if (!string.Equals(Path.GetFullPath(item.Directory), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Receita fora da biblioteca local.");
        var payload = PayloadNames.Append(IntegrityName).ToDictionary(n => n, n => ReadBounded(Path.Combine(expected, n), Limit(n)));
        return VerifyPayload(payload);
    }
    public void Export(LocalRecipe item, string destination)
    {
        var files = Load(item);
        if (!string.Equals(Path.GetExtension(destination), ".aoireceita", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A extensão deve ser .aoireceita.");
        var full = Path.GetFullPath(destination);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create)) foreach (var entry in MakePayload(files))
            { using var output = zip.CreateEntry(entry.Key, CompressionLevel.Fastest).Open(); output.Write(entry.Value); }
            // Overwrite only the path explicitly confirmed by the SaveFileDialog.
            File.Move(temp, full, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private LocalRecipe Install(Dictionary<string, byte[]> payload)
    {
        var files = VerifyPayload(payload);
        Directory.CreateDirectory(Root);
        string target = Path.Combine(Root, Key(files.Document));
        if (Directory.Exists(target))
        {
            var existing = new LocalRecipe(files.Document, target);
            var originalPayload = MakePayload(Load(existing));
            if (!PayloadNames.All(n => payload[n].SequenceEqual(originalPayload[n])))
                throw new InvalidDataException("Esta versão já existe com conteúdo diferente. Salve uma nova versão; a anterior não foi alterada.");
            return existing;
        }
        string staging = Path.Combine(Root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var entry in payload)
            {
                string path = Path.Combine(staging, entry.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(entry.Value); output.Flush(true);
            }
            Directory.Move(staging, target);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        return new(files.Document, target);
    }
    private static Dictionary<string, byte[]> MakePayload(RecipeFiles f)
    {
        var payload = new Dictionary<string, byte[]> { [PayloadNames[0]] = JsonSerializer.SerializeToUtf8Bytes(f.Document, Json), [PayloadNames[1]] = f.Clean, [PayloadNames[2]] = f.Assembled, [PayloadNames[3]] = f.OriginalAssembled, [PayloadNames[4]] = f.ValidMask };
        if (payload.Sum(x => x.Value.LongLength) > MaxTotal) throw new InvalidDataException("Receita excede 180 MB.");
        payload[IntegrityName] = JsonSerializer.SerializeToUtf8Bytes(payload.ToDictionary(x => x.Key, x => Convert.ToHexString(SHA256.HashData(x.Value))), Json);
        return payload;
    }
    private static RecipeFiles VerifyPayload(Dictionary<string, byte[]> payload)
    {
        var hashes = Deserialize<Dictionary<string, string>>(payload[IntegrityName]);
        if (hashes.Count != PayloadNames.Length || PayloadNames.Any(n => !hashes.TryGetValue(n, out string? hash) || !string.Equals(hash, Convert.ToHexString(SHA256.HashData(payload[n])), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Falha de integridade: arquivos ausentes ou conteúdo alterado.");
        var files = new RecipeFiles(Deserialize<RecipeDocument>(payload[PayloadNames[0]]), payload[PayloadNames[1]], payload[PayloadNames[2]], payload[PayloadNames[3]], payload[PayloadNames[4]]);
        ValidateFiles(files);
        return files;
    }
    public static void ValidateDocument(RecipeDocument d)
    {
        if (d.SchemaVersion != 1 || d.Source != "files" || d.CoordinateSystem != "clean-reference-pixels" || d.Status != "OfflineTest") throw new InvalidDataException("Formato de receita incompatível com este teste offline.");
        if (d.Id == Guid.Empty || d.Version < 1 || string.IsNullOrWhiteSpace(d.Model) || d.Model.Length > 100 || string.IsNullOrWhiteSpace(d.BoardRevision) || d.BoardRevision.Length > 40) throw new InvalidDataException("Informe modelo e revisão válidos.");
        if (d.Width < 64 || d.Height < 64 || d.Width > 10000 || d.Height > 10000 || (long)d.Width * d.Height > 20_000_000) throw new InvalidDataException("Dimensões inválidas.");
        if (!d.RoiReviewConfirmed || d.Rois is null || d.Rois.Count is < 1 or > 2000 || !d.Rois.Any(r => r is not null && r.Enabled)) throw new InvalidDataException("Revise as ROIs e mantenha ao menos uma região ativa (máximo 2.000).");
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in d.Rois)
        {
            if (r is null || string.IsNullOrWhiteSpace(r.Reference) || r.Reference.Length > 50 || !references.Add(r.Reference.Trim()) || r.X < 0 || r.Y < 0 || r.Width < 2 || r.Height < 2 || (long)r.X + r.Width > d.Width || (long)r.Y + r.Height > d.Height)
                throw new InvalidDataException("ROIs devem ter nomes únicos e dimensões válidas dentro da referência limpa.");
        }
        if (d.Generation is null) throw new InvalidDataException("Parâmetros ausentes.");
        RecipeImages.ValidateSettings(d.Generation);
        var a = d.Alignment;
        if (a is null || a.Homography is null || a.Homography.Length != 9 || a.Homography.Any(v => !double.IsFinite(v)) || !double.IsFinite(a.ErrorPixels) || a.ErrorPixels is < 0 or > 4 || !double.IsFinite(a.Coverage) || a.Coverage is < 0.75 or > 1 || a.Inliers < 12 || a.Matches < 18 || a.Inliers > a.Matches || a.Inliers / (double)a.Matches < 0.35)
            throw new InvalidDataException("Diagnóstico de alinhamento inválido.");
    }
    public static void ValidateFiles(RecipeFiles files)
    {
        ValidateDocument(files.Document);
        foreach (byte[] image in new[] { files.Clean, files.Assembled, files.ValidMask })
            if (RecipeImages.Dimensions(image) != (files.Document.Width, files.Document.Height)) throw new InvalidDataException("Imagem incompatível com as coordenadas das ROIs.");
        RecipeImages.Dimensions(files.OriginalAssembled);
    }
    public static string Key(RecipeDocument document) => $"{document.Id:N}-v{document.Version}";
    private static long Limit(string name) => name == IntegrityName ? 16 * 1024 : name == "receita.json" ? 1024 * 1024 : RecipeImages.MaxBytes;
    private static T Deserialize<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Documento JSON vazio.");
    private static byte[] ReadBounded(string path, long limit) { using var stream = File.OpenRead(path); return ReadBounded(stream, limit); }
    private static byte[] ReadBounded(Stream stream, long limit)
    {
        using var output = new MemoryStream(); var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) != 0) { if (output.Length + read > limit) throw new InvalidDataException("Arquivo excede o limite permitido."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
}
