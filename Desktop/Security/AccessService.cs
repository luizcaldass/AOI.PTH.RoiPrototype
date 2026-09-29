using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AOI.PTH.Desktop.Security;

public sealed record AccessRule(string Key, string Label, string Page);
public static class AccessRules
{
    public static readonly string[] Pages = ["inspection", "recipes", "defects", "history", "production", "settings", "access"];
    public static readonly string[] PageNames = ["Inspeção", "Receitas", "Defeitos", "Histórico", "Produção", "Configurações", "Perfis e acesso"];
    public static readonly AccessRule[] Actions = [
        new("recipe.create", "Criar receita", "recipes"), new("recipe.edit", "Editar / criar versão", "recipes"),
        new("recipe.import", "Importar receita", "recipes"), new("recipe.export", "Exportar receita", "recipes"),
        new("recipe.activate", "Ativar receita (requer também Inspeção)", "recipes"),
        new("inspection.photo", "Carregar foto de teste", "inspection"), new("inspection.judge", "Julgar demonstração", "inspection"),
        new("inspection.cancel", "Cancelar julgamento", "inspection"), new("inspection.finish", "Finalizar demonstração", "inspection"),
        new("inspection.reset", "Voltar / reiniciar demonstração", "inspection"),
        new("defects.edit", "Editar catálogo (integração pendente)", "defects"),
        new("settings.edit", "Salvar configurações (integração pendente)", "settings"),
        new("access.manage", "Administrar usuários e perfis", "access")];
    public static string View(string page) => "view." + page;
    public static readonly string[] Common = ["view.inspection", "inspection.judge", "inspection.cancel", "inspection.finish", "inspection.reset"];
    public static string[] All => Pages.Select(View).Concat(Actions.Select(a => a.Key)).ToArray();
    public static string RecipeAction(string action) => action switch {
        "Criar receita" => "recipe.create", "Importar receita" => "recipe.import", "Exportar receita" => "recipe.export",
        "Editor de ROIs" or "Validar versão" => "recipe.edit", "Ativar receita" => "recipe.activate",
        "Carregar foto de teste" => "inspection.photo", _ => "denied" };
}
public sealed class AccessProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public HashSet<string> Grants { get; set; } = [];
    public override string ToString() => Name;
}
public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Login { get; set; } = "";
    public Guid ProfileId { get; set; }
    public bool Enabled { get; set; } = true;
    public string Salt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int Iterations { get; set; } = 600_000;
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
public sealed class AccessDatabase
{
    public int SchemaVersion { get; set; } = 1;
    public List<AccessProfile> Profiles { get; set; } = [];
    public List<UserAccount> Users { get; set; } = [];
}
public sealed record AccountSummary(Guid Id, string Login, Guid ProfileId, string Profile, bool Enabled);

// Local station authentication. The Windows account must protect the data directory.
public sealed class AccessService(string directory)
{
    private readonly string root = Path.GetFullPath(directory);
    private Guid? currentUser;
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AOI.PTH", "acesso");
    public string LoginName => Read(db => db.Users.FirstOrDefault(u => u.Id == currentUser)?.Login ?? "");
    public string ProfileName => Read(db => db.Profiles.FirstOrDefault(p => p.Id == db.Users.FirstOrDefault(u => u.Id == currentUser)?.ProfileId)?.Name ?? "");
    public bool NeedsSetup => Read(db => db.Users.Count == 0);
    public bool Can(string permission) => Read(db => Can(db, permission));
    public void Demand(string permission) { if (!Can(permission)) throw new UnauthorizedAccessException("Seu perfil não permite esta ação."); }
    public void Logout() => currentUser = null;
    private bool Can(AccessDatabase db, string permission)
    {
        var user = db.Users.FirstOrDefault(u => u.Id == currentUser && u.Enabled);
        var profile = db.Profiles.FirstOrDefault(p => p.Id == user?.ProfileId);
        if (profile is null) return false;
        if (AccessRules.Common.Contains(permission)) return true;
        if (!profile.Grants.Contains(permission)) return false;
        var action = AccessRules.Actions.FirstOrDefault(a => a.Key == permission);
        return action is null || action.Page == "inspection" || profile.Grants.Contains(AccessRules.View(action.Page));
    }
    public void Bootstrap(string login, string password)
    {
        Change(db => {
            if (db.Users.Count != 0) throw new InvalidOperationException("O administrador inicial já foi cadastrado.");
            var profile = new AccessProfile { Name = "Administrador", Grants = AccessRules.All.ToHashSet() };
            var user = NewUser(login, profile.Id, password);
            db.Profiles.Add(profile); db.Users.Add(user);
        });
    }
    public bool Authenticate(string login, string password)
    {
        currentUser = null;
        Guid? authenticated = null;
        Change(db => {
            var user = db.Users.FirstOrDefault(u => u.Login.Equals(login.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user is null || !user.Enabled || user.LockedUntil > DateTimeOffset.UtcNow) return;
            var candidate = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(user.Salt), user.Iterations, HashAlgorithmName.SHA256, 32);
            if (!CryptographicOperations.FixedTimeEquals(candidate, Convert.FromBase64String(user.PasswordHash)))
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= 5) { user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(1); user.FailedAttempts = 0; }
                return;
            }
            user.FailedAttempts = 0; user.LockedUntil = null; authenticated = user.Id;
        });
        currentUser = authenticated;
        return authenticated.HasValue;
    }
    public IReadOnlyList<AccessProfile> Profiles() => Read(db => { RequireAdmin(db); return db.Profiles.ToArray(); });
    public IReadOnlyList<AccountSummary> Users() => Read(db => {
        RequireAdmin(db);
        return db.Users.Select(u => new AccountSummary(u.Id, u.Login, u.ProfileId, db.Profiles.Single(p => p.Id == u.ProfileId).Name, u.Enabled)).ToArray();
    });
    public void SaveProfile(Guid? id, string name, IEnumerable<string> grants)
    {
        Change(db => {
            RequireAdmin(db);
            name = name.Trim();
            if (name.Length is < 1 or > 80 || db.Profiles.Any(p => p.Id != id && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Informe um nome de perfil único (até 80 caracteres).");
            var selected = grants.ToHashSet();
            if (!selected.Any(g => g.StartsWith("view.", StringComparison.Ordinal)) || selected.Except(AccessRules.All).Any())
                throw new InvalidDataException("Selecione ao menos uma aba e somente permissões conhecidas.");
            foreach (var action in AccessRules.Actions.Where(a => selected.Contains(a.Key)))
                if (!selected.Contains(AccessRules.View(action.Page)) || action.Key == "recipe.activate" && !selected.Contains("view.inspection"))
                    throw new InvalidDataException($"A ação '{action.Label}' requer acesso à aba correspondente.");
            var profile = id.HasValue ? db.Profiles.Single(p => p.Id == id) : new AccessProfile();
            profile.Name = name; profile.Grants = selected;
            if (!id.HasValue) db.Profiles.Add(profile);
            EnsureAdministrator(db);
        });
    }
    public void SaveUser(Guid? id, string login, Guid profileId, bool enabled, string? password)
    {
        Change(db => {
            RequireAdmin(db);
            login = ValidateLogin(login);
            if (!db.Profiles.Any(p => p.Id == profileId)) throw new InvalidDataException("Selecione um perfil.");
            if (db.Users.Any(u => u.Id != id && u.Login.Equals(login, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Este login já existe.");
            var user = id.HasValue ? db.Users.Single(u => u.Id == id) : NewUser(login, profileId, password ?? "");
            if (id.HasValue && !string.IsNullOrEmpty(password)) SetPassword(user, password);
            user.Login = login; user.ProfileId = profileId; user.Enabled = enabled;
            if (!id.HasValue) db.Users.Add(user);
            EnsureAdministrator(db);
        });
    }
    private void RequireAdmin(AccessDatabase db) { if (!Can(db, "access.manage")) throw new UnauthorizedAccessException("Somente um administrador autorizado pode alterar usuários e perfis."); }
    // Save identity and a private permission profile in one transaction. Existing shared profiles stay intact.
    public void SaveUserWithPermissions(Guid? id, string login, bool enabled, string? password, IEnumerable<string> grants)
    {
        Change(db => {
            RequireAdmin(db);
            login = ValidateLogin(login);
            if (db.Users.Any(u => u.Id != id && u.Login.Equals(login, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Este login já existe.");
            var selected = grants.ToHashSet();
            if (selected.Except(AccessRules.All).Any()) throw new InvalidDataException("Permissão desconhecida.");
            selected.UnionWith(AccessRules.Common);
            foreach (var action in AccessRules.Actions.Where(a => selected.Contains(a.Key))) selected.Add(AccessRules.View(action.Page));
            var user = id.HasValue ? db.Users.Single(u => u.Id == id) : NewUser(login, Guid.Empty, password ?? "");
            if (id.HasValue && !string.IsNullOrEmpty(password)) SetPassword(user, password);
            var profile = new AccessProfile { Name = "Acesso de " + login, Grants = selected };
            db.Profiles.Add(profile);
            user.Login = login; user.Enabled = enabled; user.ProfileId = profile.Id;
            if (!id.HasValue) db.Users.Add(user);
            EnsureAdministrator(db);
        });
    }
    private static void EnsureAdministrator(AccessDatabase db)
    {
        if (!db.Users.Any(u => u.Enabled && db.Profiles.Any(p => p.Id == u.ProfileId && p.Grants.Contains("access.manage") && p.Grants.Contains("view.access"))))
            throw new InvalidDataException("Mantenha pelo menos um administrador ativo com acesso à administração.");
    }
    private static string ValidateLogin(string login)
    {
        login = login.Trim();
        if (login.Length is < 1 or > 80 || login.Any(char.IsControl)) throw new InvalidDataException("Informe um login entre 1 e 80 caracteres.");
        return login;
    }
    private static UserAccount NewUser(string login, Guid profileId, string password)
    {
        var user = new UserAccount { Login = ValidateLogin(login), ProfileId = profileId };
        SetPassword(user, password); return user;
    }
    private static void SetPassword(UserAccount user, string password)
    {
        if (password.Length is < 5 or > 128 || password.Any(c => c is < '0' or > '9'))
            throw new InvalidDataException("A senha deve conter de 5 a 128 dígitos numéricos (0–9).");
        var salt = RandomNumberGenerator.GetBytes(16);
        user.Salt = Convert.ToBase64String(salt); user.Iterations = 600_000;
        user.PasswordHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, user.Iterations, HashAlgorithmName.SHA256, 32));
        user.FailedAttempts = 0; user.LockedUntil = null;
    }
    private T Read<T>(Func<AccessDatabase, T> operation) => Locked(() => operation(Load()));
    private void Change(Action<AccessDatabase> operation) => Locked(() => {
        var db = Load(); operation(db);
        Directory.CreateDirectory(root);
        var temp = Path.Combine(root, Guid.NewGuid() + ".tmp");
        try {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, db, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true);
            }
            File.Move(temp, Path.Combine(root, "usuarios.json"), true);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
        return true;
    });
    private AccessDatabase Load()
    {
        var path = Path.Combine(root, "usuarios.json");
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 10_000_000) throw new InvalidDataException("Cadastro de acesso excede o tamanho permitido.");
        var db = JsonSerializer.Deserialize<AccessDatabase>(File.ReadAllText(path)) ?? throw new InvalidDataException("Cadastro de acesso inválido.");
        if (db.SchemaVersion != 1 || db.Users is null || db.Profiles is null || db.Users.Count == 0 ||
            db.Profiles.Any(p => p is null || p.Grants is null) || db.Users.Any(u => u is null || u.Iterations != 600_000 ||
                !db.Profiles.Any(p => p.Id == u.ProfileId))) throw new InvalidDataException("Cadastro de acesso incompatível ou incompleto.");
        return db;
    }
    private T Locked<T>(Func<T> action)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\AOI.PTH.Access." + key);
        bool entered = false;
        try { try { entered = mutex.WaitOne(TimeSpan.FromSeconds(10)); } catch (AbandonedMutexException) { entered = true; }
            if (!entered) throw new IOException("Cadastro ocupado. Tente novamente."); return action();
        } finally { if (entered) mutex.ReleaseMutex(); }
    }
}
