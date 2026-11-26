using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Acceso
{
    public static async Task Inicializar(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<RegistroDb>();
        await db.Database.MigrateAsync();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (string role in new[] { "Administrador", "Captura", "Consulta" })
            if (!await roles.RoleExistsAsync(role))
                Exigir(await roles.CreateAsync(new IdentityRole(role)));

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        if ((await users.GetUsersInRoleAsync("Administrador")).Count > 0) return;
        string name = config["Inicial:Usuario"] ?? throw new InvalidOperationException("Configura Inicial:Usuario.");
        string password = config["Inicial:Contrasena"] ?? throw new InvalidOperationException("Configura Inicial:Contrasena.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var account = new IdentityUser(name);
        Exigir(await users.CreateAsync(account, password));
        Exigir(await users.AddToRoleAsync(account, "Administrador"));
        await transaction.CommitAsync();
    }

    public static async Task<int> RestablecerAdmin(IServiceProvider services, string name)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Ejecuta el comando en una terminal interactiva.");
            return 1;
        }

        var db = services.GetRequiredService<RegistroDb>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        Console.Write("Nueva contraseña: ");
        string password = LeerClave();
        Console.Write("Confirma la contraseña: ");
        if (password != LeerClave())
        {
            Console.Error.WriteLine("Las contraseñas son distintas.");
            return 1;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await BloquearOperacion(db, users.NormalizeName(name)!);
        var account = await users.FindByNameAsync(name);
        if (account is null || !await users.IsInRoleAsync(account, "Administrador"))
        {
            Console.Error.WriteLine("La cuenta indicada no tiene el perfil Administrador.");
            return 1;
        }

        var token = await users.GeneratePasswordResetTokenAsync(account);
        var result = await users.ResetPasswordAsync(account, token, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) Console.Error.WriteLine(error.Description);
            return 1;
        }
        Exigir(await users.ResetAccessFailedCountAsync(account));
        if (account.LockoutEnabled)
            Exigir(await users.SetLockoutEndDateAsync(account, null));

        db.Eventos.Add(new Evento
        {
            Actor = Environment.UserName,
            Accion = "Recuperación de administrador",
            Detalle = account.UserName + " · Contraseña restablecida y contador reiniciado"
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine("Contraseña restablecida. Ya puedes iniciar sesión con la nueva clave.");
        return 0;
    }

    private static string LeerClave()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                password.Length--;
            else if (!char.IsControl(key.KeyChar) && password.Length < 256)
                password.Append(key.KeyChar);
        }
        Console.WriteLine();
        return password.ToString();
    }

    private static void Exigir(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    private static Task BloquearOperacion(RegistroDb db, string name) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(1, hashtext({name}))");

    public static void Registrar(RegistroDb db, HttpContext http, string action, string detail = "") =>
        db.Eventos.Add(new Evento { Actor = http.User.Identity?.Name ?? "", Accion = action, Detalle = detail });

    public static void Mapear(WebApplication app)
    {
        app.MapGet("/acceso", (HttpContext http) => Login(http));
        app.MapPost("/acceso", Entrar);
        app.MapPost("/salir", async (HttpContext http, SignInManager<IdentityUser> signIn, RegistroDb db) =>
        {
            Registrar(db, http, "Cierre de sesión");
            await db.SaveChangesAsync();
            await signIn.SignOutAsync();
            return Results.Redirect("/acceso");
        }).RequireAuthorization();
        var admin = app.MapGroup("/usuarios").RequireAuthorization("Admin");
        admin.MapGet("", Panel);
        admin.MapPost("", Administrar);
    }

    private static IResult Login(HttpContext http, string message = "") =>
        Vista.Pagina(http, "Inicio de sesión", $"""
            <form class="panel narrow" method=post>{Vista.Csrf(http)}
            <div class=grid>{Vista.Campo("Usuario", "usuario", max: 100, atributos: "required autocomplete=username")}
            {Vista.Campo("Contraseña", "clave", tipo: "password", max: 256, atributos: "required autocomplete=current-password")}</div>
            <div class=actions><button>Entrar</button><span class=alert role=alert>{Vista.E(message)}</span></div></form>
            """);

    private static async Task<IResult> Entrar(HttpContext http, UserManager<IdentityUser> users,
        SignInManager<IdentityUser> signIn, RegistroDb db)
    {
        var form = await http.Request.ReadFormAsync();
        string name = form["usuario"].ToString().Trim();
        string password = form["clave"].ToString();
        if (name.Length is 0 or > 100 || password.Length is 0 or > 256)
            return Login(http, "Captura un usuario y una contraseña válidos.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await BloquearOperacion(db, users.NormalizeName(name)!);
        var account = await users.FindByNameAsync(name);
        var result = account is null ? SignInResult.Failed
            : await signIn.CheckPasswordSignInAsync(account, password, lockoutOnFailure: true);
        if (result.IsLockedOut && account is not null && account.LockoutEnd != DateTimeOffset.MaxValue)
        {
            Exigir(await users.SetLockoutEndDateAsync(account, DateTimeOffset.MaxValue));
            Exigir(await users.UpdateSecurityStampAsync(account));
            db.Eventos.Add(new Evento { Actor = account.UserName!, Accion = "Bloqueo", Detalle = "Tres intentos fallidos consecutivos." });
        }
        db.Eventos.Add(new Evento { Actor = account?.UserName ?? name, Accion = result.Succeeded
            ? "Inicio de sesión" : result.IsLockedOut ? "Acceso bloqueado" : "Intento fallido" });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        if (result.Succeeded)
        {
            await signIn.SignInAsync(account!, isPersistent: false);
            return Results.Redirect("/permisionarios");
        }
        return Login(http, result.IsLockedOut ? "Cuenta bloqueada. Solicita el desbloqueo a un administrador."
            : "No fue posible iniciar sesión. Revisa tu usuario y contraseña.");
    }

    private static async Task<IResult> Panel(HttpContext http, UserManager<IdentityUser> users, RegistroDb db)
    {
        var accounts = await users.Users.OrderBy(row => row.UserName).ToListAsync();
        string rows = "";
        foreach (var account in accounts)
        {
            var profiles = await users.GetRolesAsync(account);
            string name = Vista.E(account.UserName);
            string status = await users.IsLockedOutAsync(account) ? "Bloqueada" : "Activa";
            rows += $"""<li><strong>{name}</strong> · {Vista.E(string.Join(", ", profiles))} · {status}</li>""";
        }
        var events = await db.Eventos.OrderByDescending(row => row.Id).Take(50).ToListAsync();
        string audit = string.Concat(events.Select(row => $"<li>{row.Fecha:yyyy-MM-dd HH:mm} UTC · {Vista.E(row.Actor)} · {Vista.E(row.Accion)} · {Vista.E(row.Detalle)}</li>"));
        return Vista.Pagina(http, "Usuarios y perfiles", $"""
            <div class=grid><section class=panel><h2>Cuentas</h2><ul>{rows}</ul></section>
            <form class=panel method=post>{Vista.Csrf(http)}<h2>Administrar cuenta</h2>
            <div class=grid>{Vista.Campo("Usuario", "usuario", max: 100, atributos: "required autocomplete=off")}
            <label>Perfil<select name=perfil><option>Consulta</option><option>Captura</option></select></label>
            {Vista.Campo("Contraseña nueva", "clave", tipo: "password", max: 256, atributos: "autocomplete=new-password")}
            {Vista.Campo("Motivo o folio", "motivo", max: 300)}</div>
            <div class=actions><button name=accion value=crear>Crear</button><button name=accion value=perfil>Cambiar perfil</button>
            <button name=accion value=desbloquear>Desbloquear</button><button name=accion value=clave>Restablecer contraseña</button></div></form></div>
            <section class=panel><h2>Bitácora reciente</h2><ul>{audit}</ul></section>
            """);
    }

    private static async Task<IResult> Administrar(HttpContext http, UserManager<IdentityUser> users, RegistroDb db)
    {
        var form = await http.Request.ReadFormAsync();
        string name = form["usuario"].ToString().Trim(), role = form["perfil"].ToString(), action = form["accion"].ToString();
        string reason = form["motivo"].ToString().Trim(), password = form["clave"].ToString();
        IResult Fail(string message) => Vista.Pagina(http, "Operación rechazada",
            $"<p class=alert>{Vista.E(message)}</p><a href=/usuarios>Volver a usuarios</a>", 400);
        if (name.Length is 0 or > 100 || role is not ("Consulta" or "Captura") || password.Length > 256 ||
            (action is "desbloquear" or "clave" && reason.Length is < 5 or > 300))
            return Fail("Verifica los datos. Para desbloquear o cambiar contraseña indica un motivo de 5 a 300 caracteres.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await BloquearOperacion(db, users.NormalizeName(name)!);
        var account = await users.FindByNameAsync(name);
        IdentityResult result;
        if (action == "crear")
        {
            if (account is not null) return Fail("La cuenta ya existe.");
            account = new IdentityUser(name);
            result = await users.CreateAsync(account, password);
            if (result.Succeeded) result = await users.AddToRoleAsync(account, role);
        }
        else
        {
            if (account is null) return Fail("La cuenta no existe.");
            switch (action)
            {
                case "perfil":
                    if (await users.IsInRoleAsync(account, "Administrador")) return Fail("El perfil administrativo está protegido.");
                    result = await users.RemoveFromRolesAsync(account, await users.GetRolesAsync(account));
                    if (result.Succeeded) result = await users.AddToRoleAsync(account, role);
                    break;
                case "desbloquear":
                    result = await users.ResetAccessFailedCountAsync(account);
                    if (result.Succeeded) result = await users.SetLockoutEndDateAsync(account, null);
                    break;
                case "clave":
                    result = await users.ResetPasswordAsync(account, await users.GeneratePasswordResetTokenAsync(account), password);
                    break;
                default:
                    return Fail("Elige una operación.");
            }
            if (result.Succeeded) result = await users.UpdateSecurityStampAsync(account);
        }
        if (!result.Succeeded) return Fail("Verifica el nombre de usuario y la política de contraseña de 12 caracteres, mayúsculas, minúsculas, números y símbolos.");
        Registrar(db, http, action switch
        {
            "crear" => "Alta de cuenta",
            "perfil" => "Cambio de perfil",
            "desbloquear" => "Desbloqueo",
            _ => "Cambio de contraseña"
        }, action is "desbloquear" or "clave" ? $"{name} · {reason}" : $"{name} · {role}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Redirect("/usuarios");
    }
}
