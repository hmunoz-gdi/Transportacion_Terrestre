using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Perfiles
{
    private const string ClaimType = "permiso";
    private static readonly (string Key, string Label, string Action)[] Modules =
    [
        ("Permisionarios", "Permisionarios", "Alta, edición y baja"),
        ("Taxistas", "Taxistas", "Registrar taxistas"),
        ("Unidades", "Unidades vehiculares", "Registrar unidades"),
        ("Documentos", "Documentos e historial", "Cargar versiones PDF")
    ];

    public static bool Protegido(string? role) => role is "Administrador" or "Consulta" or "Captura";
    public static bool Puede(ClaimsPrincipal user, string permission) =>
        user.IsInRole("Administrador") || user.IsInRole("Captura") ||
        (user.IsInRole("Consulta") && permission.EndsWith(".Consultar", StringComparison.Ordinal)) ||
        user.HasClaim(ClaimType, permission);

    private static IEnumerable<string> Permisos() =>
        Modules.SelectMany(module => new[] { module.Key + ".Consultar", module.Key + ".Cambiar" });

    public static void Configurar(WebApplicationBuilder builder)
    {
        var authorization = builder.Services.AddAuthorizationBuilder()
            .AddPolicy("Admin", policy => policy.RequireRole("Administrador"));
        foreach (string permission in Permisos())
            authorization.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
                .RequireAssertion(context => Puede(context.User, permission)));
    }

    public static void Mapear(WebApplication app)
    {
        var group = app.MapGroup("/perfiles").RequireAuthorization("Admin");
        group.MapGet("", Mostrar);
        group.MapPost("", Guardar);
    }

    public static Task BloquearEdicion(RegistroDb db) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(3, 1)");

    private static async Task<IResult> Mostrar(HttpContext http, RoleManager<IdentityRole> roles, string? id)
    {
        var profiles = await roles.Roles.OrderBy(role => role.Name).ToListAsync();
        var selected = profiles.SingleOrDefault(role => role.Id == id);
        if (id is not null && selected is null) return Results.NotFound();
        var claims = selected is null ? [] : await roles.GetClaimsAsync(selected);
        bool locked = Protegido(selected?.Name);
        string list = string.Concat(profiles.Select(role =>
            $"<a href='/perfiles?id={Uri.EscapeDataString(role.Id)}' {(role == selected ? "aria-current=page" : "")}>{Vista.E(role.Name)}</a>"));
        string Check(string permission, string label)
        {
            bool active = selected?.Name is "Administrador" or "Captura" ||
                (selected?.Name == "Consulta" && permission.EndsWith(".Consultar", StringComparison.Ordinal)) ||
                claims.Any(claim => claim.Type == ClaimType && claim.Value == permission);
            return $"<label class=check><input type=checkbox name=permiso value='{permission}' {(active ? "checked" : "")} {(locked ? "disabled" : "")}>{label}</label>";
        }
        string tree = string.Concat(Modules.Select(module => $"""
            <li><details open><summary>{module.Label}</summary><div class=checks>
            {Check(module.Key + ".Consultar", "Consultar")}
            {Check(module.Key + ".Cambiar", module.Action)}
            </div></details></li>
            """));
        return Vista.Pagina(http, "Definir perfiles", $"""
            <p><a href=/usuarios>Usuarios y perfiles</a></p>
            <div class=profile-layout>
            <section class=panel><h2>Perfiles</h2><div class=profile-list>{list}<a href=/perfiles>Nuevo perfil</a></div></section>
            <form class=panel method=post action=/perfiles>{Vista.Csrf(http)}
            <input type=hidden name=id value='{Vista.E(selected?.Id)}'>
            <h2>{(selected is null ? "Nuevo perfil" : Vista.E(selected.Name))}</h2>
            {(selected is null ? Vista.Campo("Nombre del perfil", "nombre", max: 60, atributos: "required") : "")}
            <p class=hint>Los permisos de carga y edición incluyen consulta. Los módulos de la ficha requieren consultar Permisionarios.</p>
            <details class=permission-tree open><summary>Transportación Terrestre</summary><ul>{tree}
            {(selected?.Name == "Administrador" ? "<li>Usuarios, perfiles y bitácora</li>" : "")}</ul></details>
            {(locked ? "<p>Perfil del sistema.</p>" : "<div class=actions><button>Guardar perfil</button></div>")}
            </form></div>
            """);
    }

    private static async Task<IResult> Guardar(HttpContext http, RoleManager<IdentityRole> roles,
        UserManager<IdentityUser> users, RegistroDb db)
    {
        var form = await http.Request.ReadFormAsync();
        string id = form["id"].ToString(), name = form["nombre"].ToString().Trim();
        var selected = form["permiso"].Select(value => value ?? "").ToHashSet(StringComparer.Ordinal);
        IResult Fail(string message) => Vista.Pagina(http, "Perfil no guardado",
            $"<p>{Vista.E(message)}</p><a href=/perfiles>Volver a perfiles</a>", 400);
        if (selected.Count == 0 || selected.Except(Permisos()).Any())
            return Fail("Selecciona al menos un permiso disponible.");
        foreach (var module in Modules)
            if (selected.Contains(module.Key + ".Cambiar")) selected.Add(module.Key + ".Consultar");
        selected.Add("Permisionarios.Consultar");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await BloquearEdicion(db);
        IdentityRole role;
        if (id.Length == 0)
        {
            if (name.Length is < 2 or > 60 || name.Any(char.IsControl) || await roles.RoleExistsAsync(name))
                return Fail("Indica un nombre de perfil único de 2 a 60 caracteres.");
            role = new IdentityRole(name);
            if (!(await roles.CreateAsync(role)).Succeeded) return Fail("No fue posible crear el perfil.");
        }
        else
        {
            var existing = await roles.FindByIdAsync(id);
            if (existing is null || Protegido(existing.Name)) return Fail("El perfil del sistema está protegido.");
            role = existing;
        }
        foreach (var claim in await roles.GetClaimsAsync(role))
            if (claim.Type == ClaimType && !(await roles.RemoveClaimAsync(role, claim)).Succeeded)
                return Fail("No fue posible actualizar los permisos.");
        foreach (string permission in selected.Order())
            if (!(await roles.AddClaimAsync(role, new Claim(ClaimType, permission))).Succeeded)
                return Fail("No fue posible guardar los permisos.");

        foreach (var account in await users.GetUsersInRoleAsync(role.Name!))
            if (!(await users.UpdateSecurityStampAsync(account)).Succeeded)
                return Fail("No fue posible revocar una sesión. Vuelve a intentar.");
        Acceso.Registrar(db, http, id.Length == 0 ? "Alta de perfil" : "Cambio de permisos",
            $"{role.Name} · {string.Join(", ", selected.Order())}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Redirect($"/perfiles?id={Uri.EscapeDataString(role.Id)}");
    }
}
