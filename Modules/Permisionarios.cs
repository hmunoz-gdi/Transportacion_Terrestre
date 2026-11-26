using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Permisionarios
{
    private record Domicilio(string Estado, string Municipio, string Colonia, string Cp);

    public static void Mapear(WebApplication app)
    {
        var catalog = JsonSerializer.Deserialize<Domicilio[]>(File.ReadAllText(
            Path.Combine(app.Environment.ContentRootPath, "domicilios.json"))) ?? [];
        var group = app.MapGroup("/permisionarios").RequireAuthorization("Consulta");
        group.MapGet("", Buscar);
        group.MapGet("/catalogo-domicilios", () => Results.Json(catalog));
        group.MapGet("/nuevo", (HttpContext http) => Ficha(http, new Permisionario(), true)).RequireAuthorization("Captura");
        group.MapGet("/{id:long}", async (long id, HttpContext http, RegistroDb db) =>
        {
            var row = await db.Permisionarios.FindAsync(id);
            return row is null ? Results.NotFound() : Ficha(http, row,
                row.Baja is null && (http.User.IsInRole("Captura") || http.User.IsInRole("Administrador")));
        });
        group.MapPost("/{id:long}", async (long id, HttpContext http, RegistroDb db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (id != 0) await db.BloquearFicha(id);
            var row = id == 0 ? new Permisionario() : await db.Permisionarios.FindAsync(id);
            if (row is null) return Results.NotFound();
            if (row.Baja is not null) return Results.Conflict("El registro está dado de baja.");
            var form = await http.Request.ReadFormAsync();
            string Leer(string key) => form[key].ToString().Trim();
            row.Economico = Leer("economico");
            row.Nombre = Leer("nombre");
            row.Paterno = Leer("paterno");
            row.Materno = Leer("materno");
            row.Agrupacion = Leer("agrupacion");
            row.Estado = Leer("estado");
            row.Municipio = Leer("municipio");
            row.Colonia = Leer("colonia");
            row.Cp = Leer("cp");
            row.Calle = Leer("calle");
            row.Rfc = Leer("rfc").ToUpperInvariant();
            row.Permiso = Leer("permiso");
            row.Telefonos = Leer("telefonos");
            row.Correos = Leer("correos");
            row.Observaciones = Leer("observaciones");
            string fecha = Leer("vigencia");
            row.Vigencia = DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ? date : null;
            if (row.Economico.Length is 0 or > 10 || row.Nombre.Length is 0 or > 90 ||
                new[] { row.Paterno, row.Materno, row.Agrupacion, row.Calle, row.Rfc, row.Permiso }.Any(s => s.Length > 150) ||
                new[] { row.Telefonos, row.Correos, row.Observaciones }.Any(s => s.Length > 1000) ||
                (fecha.Length > 0 && row.Vigencia is null))
                return Ficha(http, row, true, "Verifica los campos obligatorios, fechas y longitudes.");
            var address = new Domicilio(row.Estado, row.Municipio, row.Colonia, row.Cp);
            if (address != new Domicilio("", "", "", "") && !catalog.Contains(address))
                return Ficha(http, row, true, "Selecciona un domicilio completo del catálogo.");
            if (id == 0) db.Add(row);
            Acceso.Registrar(db, http, id == 0 ? "Alta de permisionario" : "Edición de permisionario", row.Economico);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Redirect($"/permisionarios/{row.Id}");
        }).RequireAuthorization("Captura");

        group.MapPost("/{id:long}/baja", async (long id, HttpContext http, RegistroDb db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.BloquearFicha(id);
            var row = await db.Permisionarios.FindAsync(id);
            if (row is null) return Results.NotFound();
            if (row.Baja is null)
            {
                row.Baja = DateTimeOffset.UtcNow;
                Acceso.Registrar(db, http, "Baja de permisionario", $"{row.Id} · {row.Economico}");
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return Results.Redirect($"/permisionarios/{id}");
        }).RequireAuthorization("Captura");
    }

    private static async Task<IResult> Buscar(HttpContext http, RegistroDb db, string? q)
    {
        q = q?.Trim();
        var query = db.Permisionarios.AsNoTracking();
        if (!string.IsNullOrEmpty(q))
        {
            if (q.Length > 150) return Results.BadRequest("La búsqueda excede 150 caracteres.");
            string pattern = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(row => EF.Functions.ILike(row.Economico, pattern) ||
                EF.Functions.ILike(row.Nombre + " " + row.Paterno + " " + row.Materno, pattern));
        }
        var records = await query.OrderBy(row => row.Economico).ThenBy(row => row.Id).Take(100).ToListAsync();
        var results = string.Concat(records.Select(row => $"""
            <article class="panel record"><div><small>{Vista.E(row.Economico)} · Ficha {row.Id}</small>
            <strong>{Vista.E($"{row.Nombre} {row.Paterno} {row.Materno}")}</strong></div>
            <div class=actions><span class=badge>{(row.Baja is null ? "Activo" : "Baja")}</span><a href=/permisionarios/{row.Id}>Abrir ficha</a></div></article>
            """));
        string add = http.User.IsInRole("Consulta") && !http.User.IsInRole("Captura") && !http.User.IsInRole("Administrador")
            ? "" : "<a class=button href=/permisionarios/nuevo>Nuevo permisionario</a>";
        return Vista.Pagina(http, "Permisionarios", $"""
            <form class=panel><div class=grid>{Vista.Campo("Número económico o nombre", "q", q, "search")}</div>
            <div class=actions><button>Buscar</button>{add}</div></form>
            <section aria-label="Resultados">{(records.Count > 0 ? results : "<p>Sin coincidencias.</p>")}</section>
            """);
    }

    private static IResult Ficha(HttpContext http, Permisionario row, bool edit, string error = "")
    {
        string link = row.Id == 0 ? "" : $"<a class=button href=/permisionarios/{row.Id}/documentos>Documentos e historial</a><a href=/permisionarios/{row.Id}/taxistas>Taxistas</a><a href=/permisionarios/{row.Id}/unidades>Unidades</a>";
        string attr = edit ? "" : "readonly";
        string Address(string label, string key, string value, string extra = "") =>
            edit ? $"<label>{label}<select name={key} data-value='{Vista.E(value)}' {extra}></select></label>"
                : Vista.Campo(label, key, value, atributos: attr);
        string fields = $"""
            <div class=grid>
            {Vista.Campo("Número económico", "economico", row.Economico, max: 10, atributos: $"required {attr}")}
            {Vista.Campo("Nombre", "nombre", row.Nombre, max: 90, atributos: $"required {attr}")}
            {Vista.Campo("Apellido paterno", "paterno", row.Paterno, atributos: attr)}
            {Vista.Campo("Apellido materno", "materno", row.Materno, atributos: attr)}
            {Vista.Campo("Agrupación", "agrupacion", row.Agrupacion, atributos: attr)}
            {Vista.Campo("RFC", "rfc", row.Rfc, atributos: attr)}
            {Vista.Campo("Número de permiso", "permiso", row.Permiso, atributos: attr)}
            {Vista.Campo("Vigencia del permiso", "vigencia", row.Vigencia?.ToString("yyyy-MM-dd"), "date", atributos: attr)}
            </div><h2 class=actions>Domicilio</h2><div class=grid>
            {Address("Estado", "estado", row.Estado)}
            {Address("Municipio o alcaldía", "municipio", row.Municipio)}
            {Address("Colonia", "colonia", row.Colonia)}
            {Address("Código postal", "cp", row.Cp)}
            {Vista.Campo("Calle", "calle", row.Calle, atributos: attr)}
            </div><h2 class=actions>Contacto y observaciones</h2><div class=grid>
            {Vista.Campo("Teléfonos (separados por coma)", "telefonos", row.Telefonos, max: 1000, atributos: attr)}
            {Vista.Campo("Correos (separados por coma)", "correos", row.Correos, max: 1000, atributos: attr)}
            <label class=wide>Observaciones<textarea name=observaciones maxlength=1000 {attr}>{Vista.E(row.Observaciones)}</textarea></label></div>
            """;
        string end = edit && row.Id > 0
            ? $"<details class=panel><summary>Dar de baja</summary><form method=post action=/permisionarios/{row.Id}/baja>{Vista.Csrf(http)}<p>El expediente se conservará para consulta.</p><button>Confirmar baja</button></form></details>"
            : "";
        string content = edit
            ? $"<form id=ficha class=panel method=post action=/permisionarios/{row.Id}>{Vista.Csrf(http)}{fields}<div class=actions><button>Guardar</button>{link}</div></form><script src=/domicilio.js defer></script>"
            : $"<section class=panel>{fields}<div class=actions>{link}</div></section>";
        return Vista.Pagina(http, row.Id == 0 ? "Nuevo permisionario" : $"Ficha {row.Economico}",
            $"<p><a href=/permisionarios>Volver al catálogo</a></p>{(row.Baja is null ? "" : "<p class=alert>Registro dado de baja.</p>")}<p class=alert role=alert>{Vista.E(error)}</p>{content}{end}", error.Length > 0 ? 400 : 200);
    }
}
