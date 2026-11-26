using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Asignaciones
{
    private const int MaximoOperadores = 3;

    private record Campo(string Clave, string Etiqueta, string Tipo = "text", int Maximo = 150);
    private static readonly Campo[] DatosOperador =
    [
        new("paterno", "Apellido paterno"), new("materno", "Apellido materno"),
        new("curp", "CURP", Maximo: 18), new("vigenciaCurp", "Vigencia de CURP", "date"),
        new("tipoTia", "Tipo de TIA"), new("numeroTia", "Número de TIA"),
        new("vigenciaApto", "Vigencia de Apto", "date"),
        new("vigenciaComprobante", "Vigencia de comprobante", "date"),
        new("alta", "Fecha de alta", "date"),
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
        app.MapGet("/operadores", (HttpContext http, RegistroDb db, string? q) => Indice("taxistas", http, db, q))
            .RequireAuthorization("Taxistas.Consultar");
        app.MapGet("/unidades", (HttpContext http, RegistroDb db, string? q) => Indice("unidades", http, db, q))
            .RequireAuthorization("Unidades.Consultar");
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
            group.MapGet("/{registro:long}/historial", (long id, long registro, HttpContext http, RegistroDb db, long? antes) =>
                HistorialFichas.Ver(http, db, id, type, registro, $"{Ruta(id, type)}/{registro}", antes));
            group.MapPost("/{registro:long}/baja", (long id, long registro, HttpContext http, RegistroDb db) =>
                Baja(id, registro, type, http, db)).RequireAuthorization(module + ".Cambiar");
            Fotografias.Mapear(group, module, type);
        }
    }

    private static async Task<IResult> Indice(string type, HttpContext http, RegistroDb db, string? q)
    {
        bool drivers = type == "taxistas";
        string title = drivers ? "Operadores" : "Unidad Vehicular";
        string route = drivers ? "/operadores" : "/unidades";
        q = q?.Trim();
        if (q?.Length > 150) return Results.BadRequest("La búsqueda excede 150 caracteres.");
        string pattern = Patron(q ?? "");
        var matches = await db.Asignaciones.FromSqlInterpolated($"""
            SELECT a.* FROM "Asignaciones" a
            JOIN "Permisionarios" p ON p."Id" = a."PermisionarioId"
            WHERE a."Tipo" = {type}
              AND (concat_ws(' ', a."Detalles"->>'marca', a."Nombre", a."Detalles"->>'paterno', a."Detalles"->>'materno') ILIKE {pattern}
              OR a."Referencia" ILIKE {pattern} OR p."Economico" ILIKE {pattern}
              OR concat_ws(' ', p."Nombre", p."Paterno", p."Materno") ILIKE {pattern})
            """).OrderBy(row => row.Nombre).ThenBy(row => row.Id).Take(100).ToListAsync();
        var holderIds = matches.Select(match => match.PermisionarioId).Distinct().ToArray();
        var holders = await db.Permisionarios.Where(row => holderIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id);
        string results = string.Concat(matches.Select(row =>
        {
            var holder = holders[row.PermisionarioId];
            return $"""
                <li><a href='{Ruta(holder.Id, type)}/{row.Id}'>{Vista.E(Permisionarios.Mayusculas(NombreVisible(row, drivers)))}</a>
                <small>{Vista.E(Permisionarios.Mayusculas(row.Referencia))} · {Vista.E(Permisionarios.Mayusculas(holder.Economico))}
                · {Vista.E(Permisionarios.Mayusculas($"{holder.Nombre} {holder.Paterno} {holder.Materno}"))}
                · {(row.Baja is null ? "Activo" : "Baja")}</small></li>
                """;
        }));
        string add = Perfiles.Puede(http.User, drivers ? "Taxistas.Cambiar" : "Unidades.Cambiar")
            ? "<a href=/permisionarios>Buscar permisionario para registrar</a>" : "";
        return Vista.Pagina(http, title, $"""
            <form class=panel method=get action='{route}'>
            {Vista.Campo(drivers ? "Operador, licencia o permisionario" : "Unidad, placa o permisionario", "q", q, "search")}
            <div class=actions><button>Buscar</button>{add}</div></form>
            <section class=panel aria-label="Resultados">
            {(matches.Count > 0 ? $"<ul class=assignment-list>{results}</ul>" : "<p>Sin coincidencias.</p>")}</section>
            """);
    }

    private static string Patron(string query) =>
        "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

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
        bool canChange = Perfiles.Puede(http.User, drivers ? "Taxistas.Cambiar" : "Unidades.Cambiar");
        int active = drivers ? await ContarOperadores(db, id) : 0;
        bool atLimit = drivers && active >= MaximoOperadores;
        bool edit = holder.Baja is null && current.Baja is null && canChange && (registro != 0 || !atLimit);
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
            $"<li><a href={route}/{row.Id}>{Vista.E(Permisionarios.Mayusculas(NombreVisible(row, drivers)))}</a><small>{Vista.E(Permisionarios.Mayusculas(row.Referencia))} · {(row.Baja is null ? "Activo" : "Baja")}</small></li>"));
        string target = registro == 0 ? route : $"{route}/{registro}";
        string form = registro == 0 && !edit ? "" : Formulario(http, current, drivers, edit, target, invalid is not null);
        string photo = registro == 0 ? (edit ? "<p class=hint>Guarda el registro para agregar su fotografía.</p>" : "") : Fotografias.Panel(http, target + "/foto", edit, id, registro, type);
        string history = registro == 0 ? "" :
            $"<p><a href='{target}/historial'>Historial de la ficha</a></p>";
        string status = current.Baja is DateTimeOffset ended
            ? $"<p class=badge>Baja · {ended:dd/MM/yyyy HH:mm} UTC</p>" : "";
        string baja = registro > 0 && canChange && current.Baja is null ? $"""
            <details class=panel><summary>Dar de baja</summary>
            <form method=post action='{target}/baja'>{Vista.Csrf(http)}
            <p>El registro seguirá disponible para consulta.</p><button>Confirmar baja</button></form></details>
            """ : "";
        return Vista.Pagina(http, title, $"""
            <section class=holder><div><small>Permisionario vinculado</small>
            <h2>{Vista.E(Permisionarios.Mayusculas($"{holder.Nombre} {holder.Paterno} {holder.Materno}"))}</h2></div>
            <div><small>Número económico</small><strong>{Vista.E(Permisionarios.Mayusculas(holder.Economico))}</strong></div>
            <a href=/permisionarios/{id}>Abrir ficha del permisionario</a></section>
            <div class=ficha-layout><div><section class=panel><h2>{title} asociados</h2>
            <form method=get action={route}>{Vista.Campo(drivers ? "Buscar operador o licencia" : "Buscar unidad o placa", "q", q, "search")}
            <div class=actions><button>Buscar</button>{(edit ? $"<a href={route}>{(drivers ? "Nuevo operador" : "Nueva unidad")}</a>" : "")}</div></form>
            {(drivers ? $"<p>{active} de {MaximoOperadores} operadores activos.</p>" : "")}
            {(atLimit && registro == 0 && canChange ? "<p>Para registrar un operador, da de baja uno activo.</p>" : "")}
            {(rows.Count > 0 ? $"<ul class=assignment-list>{list}</ul>" : "<p>Sin coincidencias.</p>")}</section>
            {status}{history}{form}{baja}</div><aside>{photo}</aside></div>
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

    private static Task<int> ContarOperadores(RegistroDb db, long id) =>
        db.Asignaciones.CountAsync(row => row.PermisionarioId == id && row.Tipo == "taxistas" && row.Baja == null);

    private static async Task<IResult> Baja(long id, long registro, string type, HttpContext http, RegistroDb db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.BloquearFicha(id);
        var row = await db.Asignaciones.SingleOrDefaultAsync(r => r.Id == registro && r.PermisionarioId == id && r.Tipo == type);
        if (row is null) return Results.NotFound();
        if (row.Baja is null)
        {
            row.Baja = DateTimeOffset.UtcNow;
            HistorialFichas.Guardar(db, http, id, type, registro, "Baja", row);
            Acceso.Registrar(db, http, type == "taxistas" ? "Baja de operador" : "Baja de unidad", $"{id} · {row.Referencia}");
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return Results.Redirect($"{Ruta(id, type)}/{registro}");
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
        if (row.Baja is not null) return Results.Conflict("El registro está dado de baja.");
        if (registro == 0 && type == "taxistas" && await ContarOperadores(db, id) >= MaximoOperadores)
            return Vista.Pagina(http, "Límite de operadores", $"""
                <p>El permisionario ya tiene tres operadores activos. Da de baja uno para registrar otro.</p>
                <a href='{Ruta(id, type)}'>Volver a Operadores</a>
                """, 409);

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

        if (registro == 0)
        {
            db.Asignaciones.Add(row);
            await db.SaveChangesAsync();
        }
        HistorialFichas.Guardar(db, http, id, type, row.Id, registro == 0 ? "Alta" : "Edición", row);
        Acceso.Registrar(db, http, $"{(registro == 0 ? "Alta" : "Edición")} de {(type == "taxistas" ? "operador" : "unidad")}",
            $"{id} · {row.Referencia}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Redirect($"{Ruta(id, type)}/{row.Id}");
    }
}
