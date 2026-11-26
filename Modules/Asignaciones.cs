using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Asignaciones
{
    private record Campo(string Clave, string Etiqueta, string Tipo = "text", int Maximo = 150);
    private static readonly Campo[] DatosOperador =
    [
        new("paterno", "Apellido paterno"), new("materno", "Apellido materno"),
        new("curp", "CURP", Maximo: 18), new("vigenciaCurp", "Vigencia de CURP", "date"),
        new("tipoTia", "Tipo de TIA"), new("numeroTia", "Número de TIA"),
        new("vigenciaApto", "Vigencia de Apto", "date"),
        new("vigenciaComprobante", "Vigencia de comprobante", "date"),
        new("alta", "Fecha de alta", "date"), new("baja", "Fecha de baja", "date"),
        new("estado", "Estado"), new("municipio", "Municipio o alcaldía"),
        new("colonia", "Colonia"), new("cp", "Código postal", Maximo: 5),
        new("calle", "Calle"), new("localidad", "Localidad"),
        new("observaciones", "Observaciones", "textarea", 1000)
    ];
    private static readonly Campo[] DatosUnidad =
    [
        new("marca", "Marca del vehículo"), new("tipoUnidad", "Tipo de unidad"),
        new("poliza", "Número de póliza"), new("anioTenencia", "Año de tenencia", Maximo: 4),
        new("ambiental", "Número de verificación medioambiental"),
        new("mecanica", "Número de verificación físico-mecánica"), new("serie", "Número de serie"),
        new("revista", "Revista", "checkbox"), new("factura", "Cuenta con factura", "checkbox"),
        new("numeroFactura", "Número de factura"), new("tarjeta", "Número de tarjeta de circulación"),
        new("sustitucion", "Sustitución", "checkbox"), new("fechaSustitucion", "Fecha de sustitución", "date"),
        new("seguro", "Cuenta con seguro", "checkbox"), new("vigenciaSeguro", "Vigencia del seguro", "date"),
        new("tenencia", "Tenencia actualizada", "checkbox")
    ];

    public static void Mapear(WebApplication app)
    {
        foreach (string route in new[] { "operadores", "unidades", "taxistas" })
        {
            string type = route == "unidades" ? "unidades" : "taxistas";
            string module = type == "taxistas" ? "Taxistas" : "Unidades";
            var group = app.MapGroup($"/permisionarios/{{id:long}}/{route}").RequireAuthorization(module + ".Consultar");
            group.MapGet("", (long id, HttpContext http, RegistroDb db, string? q) => Panel(id, 0, type, http, db, q));
            group.MapGet("/{registro:long}", (long id, long registro, HttpContext http, RegistroDb db) =>
                Panel(id, registro, type, http, db, null));
            group.MapPost("", (long id, HttpContext http, RegistroDb db) => Guardar(id, 0, type, http, db))
                .RequireAuthorization(module + ".Cambiar");
            group.MapPost("/{registro:long}", (long id, long registro, HttpContext http, RegistroDb db) => Guardar(id, registro, type, http, db))
                .RequireAuthorization(module + ".Cambiar");
            Fotografias.Mapear(group, module, type);
        }
    }

    private static string Ruta(long id, string type) =>
        $"/permisionarios/{id}/{(type == "taxistas" ? "operadores" : "unidades")}";

    private static async Task<IResult> Panel(long id, long registro, string type, HttpContext http, RegistroDb db,
        string? q, Asignacion? invalid = null)
    {
        var holder = await db.Permisionarios.FindAsync(id);
        if (holder is null) return Results.NotFound();
        var current = invalid ?? (registro == 0 ? new Asignacion() : await db.Asignaciones.SingleOrDefaultAsync(row =>
            row.Id == registro && row.PermisionarioId == id && row.Tipo == type));
        if (current is null) return Results.NotFound();

        bool drivers = type == "taxistas";
        bool edit = holder.Baja is null && Perfiles.Puede(http.User, drivers ? "Taxistas.Cambiar" : "Unidades.Cambiar");
        string route = Ruta(id, type);
        string title = drivers ? "Operadores" : "Unidad Vehicular";
        q = q?.Trim();
        if (q?.Length > 150) return Results.BadRequest("La búsqueda excede 150 caracteres.");
        var query = db.Asignaciones.Where(row => row.PermisionarioId == id && row.Tipo == type);
        if (!string.IsNullOrEmpty(q))
        {
            string escaped = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            string pattern = $"%{escaped}%";
            query = db.Asignaciones.FromSqlInterpolated($"""
                SELECT * FROM "Asignaciones"
                WHERE "PermisionarioId" = {id} AND "Tipo" = {type}
                AND (concat_ws(' ', "Nombre", "Detalles"->>'paterno', "Detalles"->>'materno')
                    ILIKE {pattern} OR "Referencia" ILIKE {pattern}
                    OR concat_ws(' ', "Detalles"->>'marca', "Nombre") ILIKE {pattern})
                """);
        }
        var rows = await query.OrderBy(row => row.Nombre).ThenBy(row => row.Id).Take(100).ToListAsync();
        string list = string.Concat(rows.Select(row =>
            $"<li><a href={route}/{row.Id}>{Vista.E(Permisionarios.Mayusculas(NombreVisible(row, drivers)))}</a><small>{Vista.E(Permisionarios.Mayusculas(row.Referencia))}</small></li>"));
        string target = registro == 0 ? route : $"{route}/{registro}";
        string form = registro == 0 && !edit ? "" : Formulario(http, current, drivers, edit, target, invalid is not null);
        string photo = registro == 0 ? (edit ? "<p class=hint>Guarda el registro para agregar su fotografía.</p>" : "") : Fotografias.Panel(http, target + "/foto", edit, id, registro);
        return Vista.Pagina(http, title, $"""
            <section class=holder><div><small>Permisionario vinculado</small>
            <h2>{Vista.E(Permisionarios.Mayusculas($"{holder.Nombre} {holder.Paterno} {holder.Materno}"))}</h2></div>
            <div><small>Número económico</small><strong>{Vista.E(Permisionarios.Mayusculas(holder.Economico))}</strong></div>
            <a href=/permisionarios/{id}>Abrir ficha del permisionario</a></section>
            <div class=ficha-layout><div><section class=panel><h2>{title} asociados</h2>
            <form method=get action={route}>{Vista.Campo(drivers ? "Buscar operador o licencia" : "Buscar unidad o placa", "q", q, "search")}
            <div class=actions><button>Buscar</button>{(edit ? $"<a href={route}>{(drivers ? "Nuevo operador" : "Nueva unidad")}</a>" : "")}</div></form>
            {(rows.Count > 0 ? $"<ul class=assignment-list>{list}</ul>" : "<p>Sin coincidencias.</p>")}</section>{form}</div>
            <aside>{photo}</aside></div>
            """, invalid is null ? 200 : 400);
    }

    private static Dictionary<string, string> LeerDetalles(Asignacion row) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.Detalles) ?? [];

    private static string NombreVisible(Asignacion row, bool drivers)
    {
        var details = LeerDetalles(row);
        return drivers
            ? string.Join(" ", new[] { row.Nombre, details.GetValueOrDefault("paterno"), details.GetValueOrDefault("materno") }.Where(s => !string.IsNullOrEmpty(s)))
            : $"{details.GetValueOrDefault("marca")} {row.Nombre}".Trim();
    }

    private static string Formulario(HttpContext http, Asignacion row, bool drivers, bool edit, string target, bool invalid)
    {
        var details = LeerDetalles(row);
        string status = invalid ? "<p class=alert role=alert>Verifica los campos requeridos, fechas y longitudes.</p>" : "";
        string attr = edit ? "" : "readonly";
        string start = edit ? $"<form id=ficha class=\"panel datos-padron\" data-captura method=post action={target}>{Vista.Csrf(http)}" : "<section class=\"panel datos-padron\">";
        string end = edit ? "<div class=actions><button>Guardar</button></div></form>" : "</section>";
        if (drivers && edit) end += "<script src=/domicilio.js defer></script>";
        string fields = string.Concat((drivers ? DatosOperador : DatosUnidad).Select(field =>
        {
            string value = details.GetValueOrDefault(field.Clave, "");
            if (edit && field.Clave is "estado" or "municipio" or "colonia" or "cp")
                return $"<label>{field.Etiqueta}<select name={field.Clave} data-value='{Vista.E(value)}'></select></label>";
            return field.Tipo switch
            {
                "checkbox" => $"<label class=check><input type=checkbox name='{field.Clave}' value=1 {(value == "1" ? "checked" : "")} {(edit ? "" : "disabled")}>{field.Etiqueta}</label>",
                "textarea" => $"<label class=wide>{field.Etiqueta}<textarea name='{field.Clave}' maxlength={field.Maximo} {attr}>{Vista.E(value)}</textarea></label>",
                _ => Vista.Campo(field.Etiqueta, field.Clave, value, field.Tipo, field.Maximo, attr)
            };
        }));
        return $"""
            {start}<h2>{(row.Id == 0 ? (drivers ? "Registrar operador" : "Registrar unidad") : Vista.E(Permisionarios.Mayusculas(NombreVisible(row, drivers))))}</h2>
            {status}<div class=grid>
            {Vista.Campo(drivers ? "Nombre" : "Modelo", "nombre", row.Nombre, max: 150, atributos: $"required {attr}")}
            {Vista.Campo(drivers ? "Número de licencia" : "Placa", "referencia", row.Referencia, max: 50, atributos: $"required {attr}")}
            {Vista.Campo(drivers ? "Vigencia de licencia" : "Vigencia de tarjeta de circulación", "vigencia",
                row.Vigencia?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "date", atributos: attr)}
            {fields}</div>{end}
            """;
    }

    private static async Task<IResult> Guardar(long id, long registro, string type, HttpContext http, RegistroDb db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.BloquearFicha(id);
        var holder = await db.Permisionarios.FindAsync(id);
        if (holder is null) return Results.NotFound();
        if (holder.Baja is not null) return Results.Conflict("El expediente está dado de baja.");
        var row = registro == 0 ? new Asignacion { PermisionarioId = id, Tipo = type } :
            await db.Asignaciones.SingleOrDefaultAsync(r => r.Id == registro && r.PermisionarioId == id && r.Tipo == type);
        if (row is null) return Results.NotFound();

        var form = await http.Request.ReadFormAsync();
        row.Nombre = Permisionarios.Mayusculas(form["nombre"].ToString());
        row.Referencia = Permisionarios.Mayusculas(form["referencia"].ToString());
        bool Date(string value, out DateOnly date) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        string validity = form["vigencia"].ToString();
        row.Vigencia = Date(validity, out var date) ? date : null;
        bool valid = row.Nombre.Length is > 0 and <= 150 && row.Referencia.Length is > 0 and <= 50
            && (validity.Length == 0 || row.Vigencia is not null);
        var details = new Dictionary<string, string>();
        foreach (var field in (type == "taxistas" ? DatosOperador : DatosUnidad))
        {
            string value = Permisionarios.Mayusculas(form[field.Clave].ToString());
            if (field.Tipo == "checkbox") value = value == "1" ? "1" : "";
            valid &= value.Length <= field.Maximo && (value.Length == 0 || field.Tipo != "date" || Date(value, out _));
            if (value.Length > 0) details[field.Clave] = value;
        }
        if (type == "taxistas")
        {
            var address = new Permisionarios.Domicilio(details.GetValueOrDefault("estado", ""), details.GetValueOrDefault("municipio", ""),
                details.GetValueOrDefault("colonia", ""), details.GetValueOrDefault("cp", ""));
            valid &= address == new Permisionarios.Domicilio("", "", "", "")
                || http.RequestServices.GetRequiredService<Permisionarios.Domicilio[]>().Contains(address);
        }
        row.Detalles = JsonSerializer.Serialize(details);
        if (!valid) return await Panel(id, registro, type, http, db, null, row);

        if (registro == 0) db.Asignaciones.Add(row);
        Acceso.Registrar(db, http, $"{(registro == 0 ? "Alta" : "Edición")} de {(type == "taxistas" ? "operador" : "unidad")}",
            $"{id} · {row.Referencia}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Redirect($"{Ruta(id, type)}/{row.Id}");
    }
}
