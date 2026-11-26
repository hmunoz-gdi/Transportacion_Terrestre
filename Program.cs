using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Transportacion;

bool recuperar = args.Length == 2 && args[0] == "restablecer-admin";
var builder = WebApplication.CreateBuilder(recuperar ? [] : args);
var connection = builder.Configuration.GetConnectionString("Transportacion")
    ?? throw new InvalidOperationException("Configura ConnectionStrings:Transportacion.");
builder.Services.AddDbContext<RegistroDb>(options => options.UseNpgsql(connection));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 3;
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<RegistroDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/acceso";
    options.AccessDeniedPath = "/denegado";
    options.Cookie.Name = "Transportacion.Sesion";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Consulta", policy => policy.RequireRole("Consulta", "Captura", "Administrador"))
    .AddPolicy("Captura", policy => policy.RequireRole("Captura", "Administrador"))
    .AddPolicy("Admin", policy => policy.RequireRole("Administrador"));
builder.Services.AddAntiforgery();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = 26 * 1024 * 1024);
if (recuperar)
{
    await using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    Environment.ExitCode = await Acceso.RestablecerAdmin(scope.ServiceProvider, args[1]);
    return;
}

builder.Services.AddSingleton(new RepositorioPdf(builder.Configuration["Documentos:Raiz"]
    ?? throw new InvalidOperationException("Configura Documentos:Raiz."), builder.Environment.WebRootPath));

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (HttpMethods.IsPost(context.Request.Method))
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("La solicitud venció. Vuelve a abrir el formulario.");
            return;
        }
    }
    await next();
});
using (var scope = app.Services.CreateScope())
{
    await Acceso.Inicializar(scope.ServiceProvider, app.Configuration);
}
app.MapGet("/", () => Results.Redirect("/permisionarios"));
app.MapGet("/denegado", (HttpContext http) => Vista.Pagina(http, "Acceso denegado", "<p>Tu perfil no tiene permiso para esta operación.</p>"));
Acceso.Mapear(app);
Permisionarios.Mapear(app);
Documentos.Mapear(app);
Asignaciones.Mapear(app);
app.Run();
