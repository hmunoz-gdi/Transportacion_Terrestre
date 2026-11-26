using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Caratulas
{
    public static void Mapear(WebApplication app)
    {
        var config = app.Configuration["Caratula:UrlBase"];
        if (string.IsNullOrWhiteSpace(config) && app.Environment.IsDevelopment())
            config = "http://localhost:5080";
        string? origin = null;
        if (config is not null && Uri.TryCreate(config, UriKind.Absolute, out var url) &&
            (url.Scheme == "https" || (app.Environment.IsDevelopment() && url.Scheme == "http" && url.IsLoopback)) &&
            url.AbsoluteUri.Length <= 180 && url.Query.Length == 0 && url.Fragment.Length == 0 && url.UserInfo.Length == 0)
            origin = url.AbsoluteUri.TrimEnd('/');

        var pdf = new CaratulaPdf(app.Environment.ContentRootPath);
        var group = app.MapGroup("/permisionarios/{id:long}/caratula").RequireAuthorization("Permisionarios.Consultar");
        group.MapGet("", (long id, HttpContext http, RegistroDb db, RepositorioPdf store, Guid? antes) =>
            Panel(id, http, db, store, origin, antes));
        group.MapPost("", (long id, HttpContext http, RegistroDb db, RepositorioPdf store) =>
            Emitir(id, http, db, store, pdf, origin)).RequireAuthorization("Permisionarios.Cambiar");
        group.MapGet("/{folio:guid}", Descargar);
        app.MapGet("/verificar-caratula/{codigo}", Verificar).AllowAnonymous();
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));

    private static async Task<IResult> Panel(long id, HttpContext http, RegistroDb db, RepositorioPdf store, string? origin, Guid? antes)
    {
        var holder = await db.Permisionarios.FindAsync(id);
        if (holder is null) return Results.NotFound();
        var route = $"/permisionarios/{id}/caratula";
        var cursor = antes is Guid previous ? await db.Caratulas.SingleOrDefaultAsync(row => row.PermisionarioId == id && row.Id == previous) : null;
        var query = db.Caratulas.Where(row => row.PermisionarioId == id);
        if (cursor is not null)
            query = query.Where(row => row.Fecha < cursor.Fecha || (row.Fecha == cursor.Fecha && row.Id.CompareTo(cursor.Id) < 0));
        var issued = await query.OrderByDescending(row => row.Fecha).ThenByDescending(row => row.Id).Take(26).ToListAsync();
        string list = string.Concat(issued.Take(25).Select(row =>
            $"<li><a href='{route}/{row.Id:D}'>Descargar folio {row.Id:N}</a><small>{CaratulaPdf.FechaLocal(row.Fecha):dd/MM/yyyy HH:mm} · {Vista.E(row.Usuario)}</small></li>"));
        string older = issued.Count > 25 ? $"<p><a href='{route}?antes={issued[24].Id:D}'>Ver emisiones anteriores</a></p>" : "";
        string form = "";
        if (holder.Baja is null && Perfiles.Puede(http.User, "Permisionarios.Cambiar"))
        {
            var units = await db.Asignaciones.Where(row => row.PermisionarioId == id && row.Tipo == "unidades" && row.Baja == null)
                .OrderBy(row => row.Referencia).ToListAsync();
            var photo = await Fotografias.RutaTitular(db, store, id);
            if (origin is null)
                form = "<p>Configura una URL base de verificación en el servidor para generar la carátula.</p>";
            else if (photo is null)
                form = $"<p>Agrega la fotografía en <a href='/permisionarios/{id}'>la ficha del permisionario</a> antes de emitir.</p>";
            else
            {
                string options = string.Concat(units.Select(row =>
                    $"<option value='{row.Id}'>{Vista.E($"{row.Referencia} · {row.Nombre}")}</option>"));
                string vehicle = units.Count == 0 ? "<p>Se emitirá con los datos vehiculares como S/D.</p>" :
                    $"<label>Unidad vehicular<select name=unidad required><option value=''>Seleccionar</option>{options}</select></label>";
                form = $"""
                    <form class=panel method=post action='{route}'>{Vista.Csrf(http)}
                    <h2>Emitir carátula</h2>{vehicle}<div class=actions><button>Generar PDF con QR</button></div></form>
                    """;
            }
        }
        return Vista.Pagina(http, "Carátula", $"""
            <p><a href='/permisionarios/{id}'>Volver a Permisionarios</a></p>
            <section class=holder><div><small>Permisionario · {Vista.E(holder.Economico)}</small>
            <h2>{Vista.E($"{holder.Nombre} {holder.Paterno} {holder.Materno}".Trim())}</h2></div></section>
            {form}<section class=panel><h2>Carátulas emitidas</h2>
            {(issued.Count > 0 ? $"<ul>{list}</ul>" : "<p>Sin emisiones registradas.</p>")}{older}</section>
            """);
    }

    private static async Task<IResult> Emitir(long id, HttpContext http, RegistroDb db, RepositorioPdf store, CaratulaPdf pdf, string? origin)
    {
        if (origin is null) return Results.Conflict("Configura Caratula:UrlBase para emitir carátulas.");
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var token = http.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.BloquearFicha(id, token);
        var holder = await db.Permisionarios.FindAsync([id], token);
        if (holder is null) return Results.NotFound();
        if (holder.Baja is not null) return Results.Conflict("El permisionario está dado de baja.");
        var units = db.Asignaciones.Where(row => row.PermisionarioId == id && row.Tipo == "unidades" && row.Baja == null);
        Asignacion? vehicle = null;
        string selected = form["unidad"].ToString();
        if (selected.Length != 0)
        {
            if (long.TryParse(selected, out long unit))
                vehicle = await units.SingleOrDefaultAsync(row => row.Id == unit, token);
            if (vehicle is null) return Results.BadRequest("Selecciona una unidad activa de este permisionario.");
        }
        else if (await units.AnyAsync(token))
            return Results.BadRequest("Selecciona la unidad que debe aparecer en la carátula.");

        var photo = await Fotografias.RutaTitular(db, store, id);
        if (photo is null) return Results.Conflict("La ficha requiere una fotografía para emitir la carátula.");
        string code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var emission = new CaratulaEmitida
        {
            Id = Guid.NewGuid(), PermisionarioId = id, UnidadId = vehicle?.Id, Fecha = DateTimeOffset.UtcNow,
            Usuario = http.User.Identity?.Name ?? "", CodigoHash = Hash(code)
        };
        MemoryStream generated;
        try { generated = pdf.Generar(holder, vehicle, photo, emission, $"{origin}/verificar-caratula/{code}"); }
        catch (InvalidDataException error)
        {
            return Vista.Pagina(http, "Carátula", $"<p>{Vista.E(error.Message)}</p><a href='/permisionarios/{id}'>Volver a la ficha</a>", 422);
        }
        await using var file = generated;
        emission.PdfHash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        file.Position = 0;
        await RepositorioPdf.GuardarArchivo(store.RutaCaratula(id, emission.Id), file, token);
        db.Caratulas.Add(emission);
        Acceso.Registrar(db, http, "Emisión de carátula", $"{id} · {emission.Id:N}");
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(CancellationToken.None);
        return Results.Redirect($"/permisionarios/{id}/caratula/{emission.Id:D}");
    }

    private static async Task<IResult> Descargar(long id, Guid folio, RegistroDb db, RepositorioPdf store)
    {
        if (!await db.Caratulas.AnyAsync(row => row.Id == folio && row.PermisionarioId == id))
            return Results.NotFound();
        string path = store.RutaCaratula(id, folio);
        return File.Exists(path)
            ? Results.File(path, "application/pdf", $"Caratula-{folio:N}.pdf")
            : Results.NotFound();
    }

    private static async Task<IResult> Verificar(string codigo, HttpContext http, RegistroDb db)
    {
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        var emission = codigo.Length == 43 && codigo.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? await db.Caratulas.AsNoTracking().SingleOrDefaultAsync(row => row.CodigoHash == Hash(codigo)) : null;
        if (emission is null)
            return Vista.Pagina(http, "Verificar carátula", "<p class=alert>Código no reconocido.</p>", 404);
        bool ended = await db.Permisionarios.AnyAsync(row => row.Id == emission.PermisionarioId && row.Baja != null);
        if (!ended && emission.UnidadId is long unit)
            ended = await db.Asignaciones.AnyAsync(row => row.Id == unit && row.Baja != null);
        string original = Perfiles.Puede(http.User, "Permisionarios.Consultar")
            ? $"<p><a href='/permisionarios/{emission.PermisionarioId}/caratula/{emission.Id:D}'>Descargar el PDF emitido para cotejar la copia impresa</a></p>"
            : "<p>Si la copia es impresa, solicita a un usuario autorizado que coteje los datos y la fotografía con el original.</p>";
        return Vista.Pagina(http, "Verificar carátula", $"""
            <section class=panel><h2>Emisión registrada</h2>
            <p>Folio <strong>{emission.Id:N}</strong></p>
            <p>Emitida el {CaratulaPdf.FechaLocal(emission.Fecha):dd/MM/yyyy HH:mm} (Ciudad de México).</p>
            {(ended ? "<p class=alert>El permisionario o la unidad vinculada está dado de baja.</p>" : "")}
            {original}
            <label>Comparar archivo PDF<input id=pdf-verificar type=file accept="application/pdf,.pdf" data-hash='{emission.PdfHash}'></label>
            <p id=pdf-verificado role=status></p></section><script src=/verificar-caratula.js defer></script>
            """);
    }
}
