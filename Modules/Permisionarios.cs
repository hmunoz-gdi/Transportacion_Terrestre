using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Permisionarios
{
    internal static readonly string[] Agrupaciones =
    [
        "NUEVA IMAGEN", "PORTO TAXI", "YELLOW CAB", "SITIO 300", "AERO TAXI", "TAXI RIDE",
        "T & M MOVILIDAD", "CONFORT UNLIMITED", "MARIA GUADALUPE ESPINOSA MARTINEZ", "PRHO TAXI", "PSTA PLUS"
    ];
    private static readonly Regex NombreValido = new(@"^[\p{L}\p{M} .’'\-]+$", RegexOptions.NonBacktracking);
    private static readonly Regex EconomicoValido = new(@"^[A-Z0-9-]{1,10}$", RegexOptions.NonBacktracking);
    private static readonly Regex PermisoValido = new(@"^[A-ZÑ0-9 ./_-]{1,50}$", RegexOptions.NonBacktracking);
    private static readonly Regex TelefonoValido = new(@"^[0-9]{10}$", RegexOptions.NonBacktracking);
    private static readonly Regex RfcValido = new(@"^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$", RegexOptions.NonBacktracking);
    private static readonly Regex CorreoValido = new(@"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+$", RegexOptions.NonBacktracking);

    internal record Domicilio(string Estado, string Municipio, string Colonia, string Cp);

    internal static Domicilio[] CargarCatalogo(string root) =>
        (JsonSerializer.Deserialize<Domicilio[]>(File.ReadAllText(Path.Combine(root, "domicilios.json"))) ?? [])
            .Select(row => new Domicilio(Mayusculas(row.Estado), Mayusculas(row.Municipio), Mayusculas(row.Colonia), row.Cp.Trim()))
            .Distinct().ToArray();

    internal static string Mayusculas(string value) => value.Trim().ToUpperInvariant();

    private static string[] Contactos(string value) =>
        value.Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string[] Contactos(IFormCollection form, string key)
    {
        var values = form[key];
        if (values.Count == 1 && !form.ContainsKey("contactos_en_filas"))
            return Contactos(values.ToString());
        return values.Append(form[key + "-captura"].ToString())
            .Select(value => value?.Trim() ?? "").Where(value => value.Length > 0).ToArray();
    }

    public static void Mapear(WebApplication app)
    {
        var catalog = app.Services.GetRequiredService<Domicilio[]>();
        var group = app.MapGroup("/permisionarios").RequireAuthorization("Permisionarios.Consultar");
        group.MapGet("", Buscar);
        Fotografias.Mapear(group, "Permisionarios");
        group.MapGet("/{id:long}/historial", (long id, HttpContext http, RegistroDb db, long? antes) =>
            HistorialFichas.Ver(http, db, id, HistorialFichas.Titular, 0, $"/permisionarios/{id}", antes));
        group.MapGet("/catalogo-domicilios", () => Results.Json(catalog));
        group.MapGet("/nuevo", (HttpContext http) => Ficha(http, new Permisionario(), true)).RequireAuthorization("Permisionarios.Cambiar");
        group.MapGet("/{id:long}", async (long id, HttpContext http, RegistroDb db) =>
        {
            var row = await db.Permisionarios.FindAsync(id);
            return row is null ? Results.NotFound() : Ficha(http, row,
                row.Baja is null && Perfiles.Puede(http.User, "Permisionarios.Cambiar"));
        });
        group.MapPost("/{id:long}", async (long id, HttpContext http, RegistroDb db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (id != 0) await db.BloquearFicha(id);
            var row = id == 0 ? new Permisionario() : await db.Permisionarios.FindAsync(id);
            if (row is null) return Results.NotFound();
            if (row.Baja is not null) return Results.Conflict("El registro está dado de baja.");
            var form = await http.Request.ReadFormAsync();
            string previousGroup = Mayusculas(row.Agrupacion);
            string Leer(string key) => Mayusculas(form[key].ToString());
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
            row.Rfc = Leer("rfc");
            row.Permiso = Leer("permiso");
            var phones = Contactos(form, "telefonos");
            var mails = Contactos(form, "correos");
            row.Telefonos = string.Join(", ", phones);
            row.Correos = string.Join(", ", mails);
            row.Observaciones = Leer("observaciones");
            string fecha = Leer("vigencia"), nacimiento = Leer("nacimiento");
            static DateOnly? Fecha(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ? date : null;
            row.Vigencia = Fecha(fecha);
            row.Nacimiento = Fecha(nacimiento);
            string error = EconomicoValido.IsMatch(row.Economico)
                ? "" : "El número económico requiere de 1 a 10 letras, dígitos o guiones.";
            if (row.Nombre.Length == 0 || new[] { row.Nombre, row.Paterno, row.Materno }
                .Any(name => name.Length > 90 || (name.Length > 0 && !NombreValido.IsMatch(name))))
                error += " Captura nombres y apellidos de hasta 90 caracteres con letras, espacios, puntos, apóstrofos o guiones.";
            if (row.Agrupacion.Length > 0 && row.Agrupacion != previousGroup && !Agrupaciones.Contains(row.Agrupacion))
                error += " Selecciona una agrupación de la lista.";
            if (row.Rfc.Length > 0 && !RfcValido.IsMatch(row.Rfc))
                error += " El RFC debe tener 13 caracteres para persona física o 12 para persona moral, incluido el sufijo fiscal.";
            if (phones.Length > 10) error += " El máximo es de 10 teléfonos.";
            if (phones.Any(phone => !TelefonoValido.IsMatch(phone)))
                error += " Corrige o quita los teléfonos que no tengan exactamente 10 dígitos.";
            var emailAddress = new EmailAddressAttribute();
            if (mails.Length > 10) error += " El máximo es de 10 correos.";
            if (mails.Any(mail => mail.Length > 254 || !CorreoValido.IsMatch(mail) || !emailAddress.IsValid(mail)))
                error += " Corrige o quita las direcciones de correo con formato inválido.";
            if (row.Permiso.Length > 0 && !PermisoValido.IsMatch(row.Permiso))
                error += " El número de permiso admite hasta 50 letras, dígitos, espacios, puntos, diagonales o guiones.";
            if (row.Calle.Length > 150 || row.Calle.Any(char.IsControl) ||
                row.Observaciones.Length > 1000 || (fecha.Length > 0 && row.Vigencia is null) ||
                (nacimiento.Length > 0 && (row.Nacimiento is null || row.Nacimiento > DateOnly.FromDateTime(DateTime.Today))))
                error += " Revisa las fechas y las longitudes de calle, permiso y observaciones.";
            if (error.Length > 0) return Ficha(http, row, true, error.Trim(), phones, mails);
            var address = new Domicilio(row.Estado, row.Municipio, row.Colonia, row.Cp);
            if (address != new Domicilio("", "", "", "") && !catalog.Contains(address))
                return Ficha(http, row, true, "Selecciona un domicilio completo del catálogo.", phones, mails);
            if (id == 0) db.Add(row);
            Acceso.Registrar(db, http, id == 0 ? "Alta de permisionario" : "Edición de permisionario", row.Economico);
            if (id == 0) await db.SaveChangesAsync();
            HistorialFichas.Guardar(db, http, row.Id, HistorialFichas.Titular, 0, id == 0 ? "Alta" : "Edición", row);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Redirect($"/permisionarios/{row.Id}");
        }).RequireAuthorization("Permisionarios.Cambiar");

        group.MapPost("/{id:long}/baja", async (long id, HttpContext http, RegistroDb db) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.BloquearFicha(id);
            var row = await db.Permisionarios.FindAsync(id);
            if (row is null) return Results.NotFound();
            if (row.Baja is null)
            {
                row.Baja = DateTimeOffset.UtcNow;
                HistorialFichas.Guardar(db, http, id, HistorialFichas.Titular, 0, "Baja", row);
                Acceso.Registrar(db, http, "Baja de permisionario", $"{row.Id} · {row.Economico}");
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return Results.Redirect($"/permisionarios/{id}");
        }).RequireAuthorization("Permisionarios.Cambiar");
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
            <article class="panel record"><div><small>{Vista.E(Mayusculas(row.Economico))} · Ficha {row.Id}</small>
            <strong>{Vista.E(Mayusculas($"{row.Nombre} {row.Paterno} {row.Materno}"))}</strong></div>
            <div class=actions><span class=badge>{(row.Baja is null ? "Activo" : "Baja")}</span><a href=/permisionarios/{row.Id}>Abrir ficha</a></div></article>
            """));
        string add = !Perfiles.Puede(http.User, "Permisionarios.Cambiar")
            ? "" : "<a class=button href=/permisionarios/nuevo>Nuevo permisionario</a>";
        return Vista.Pagina(http, "Permisionarios", $"""
            <form class=panel><div class=grid>{Vista.Campo("Número económico o nombre", "q", q, "search")}</div>
            <div class=actions><button>Buscar</button>{add}</div></form>
            <section aria-label="Resultados">{(records.Count > 0 ? results : "<p>Sin coincidencias.</p>")}</section>
            """);
    }

    private static string SelectorAgrupacion(string actual)
    {
        var options = Agrupaciones.AsEnumerable();
        if (actual.Length > 0 && !Agrupaciones.Contains(actual)) options = options.Prepend(actual);
        return "<label>Agrupación<select name=agrupacion><option value=''>Seleccionar</option>" +
            string.Concat(options.Select(option => $"<option {(option == actual ? "selected" : "")}>{Vista.E(option)}</option>")) +
            "</select></label>";
    }

    private static string Cuadricula(string name, string label, string values, bool edit, bool phone, string[]? submitted = null)
    {
        var entries = submitted ?? Contactos(values);
        string input = phone
            ? Vista.Campo(label, name + "-captura", tipo: "tel", max: 10,
                atributos: "data-entry inputmode=numeric pattern='[0-9]{10}' autocomplete=tel-national")
            : Vista.Campo(label, name + "-captura", tipo: "email", max: 254, atributos: "data-entry autocomplete=email");
        string Row(string value) => edit
            ? $"<tr><td><span data-value>{Vista.E(value)}</span><input type=hidden name='{name}' value='{Vista.E(value)}'></td>" +
              $"<td class=contact-action><button type=button class=quiet data-remove aria-label='Quitar {label.ToLowerInvariant()} {Vista.E(value)}'>Quitar</button></td></tr>"
            : $"<tr><td>{Vista.E(value)}</td></tr>";
        return $"""
            <section class=contactos data-contactos data-label='{label.ToLowerInvariant()}'>
            {(edit ? $"<div class=captura-contacto>{input}<button type=button class=quiet data-add>Agregar {label.ToLowerInvariant()}</button></div><template>{Row("")}</template>" : "")}
            <table><thead><tr><th scope=col>{label}</th>
            {(edit ? "<th scope=col class=contact-action>Acción</th>" : "")}</tr></thead>
            <tbody>{string.Concat(entries.Select(Row))}</tbody></table>
            <p class=contact-status data-empty {(entries.Length > 0 ? "hidden" : "")}>Sin registros</p>
            <p class=sr-visually-hidden data-status aria-live=polite></p></section>
            """;
    }

    private static IResult Ficha(HttpContext http, Permisionario row, bool edit, string error = "", string[]? phones = null, string[]? mails = null)
    {
        string link = "";
        if (row.Id > 0)
        {
            link = $"<a href=/permisionarios/{row.Id}/historial>Historial de la ficha</a>";
            if (Perfiles.Puede(http.User, "Documentos.Consultar"))
                link += $"<a href=/permisionarios/{row.Id}/documentos>Documentos e historial</a>";
            if (Perfiles.Puede(http.User, "Taxistas.Consultar"))
                link += $"<a href=/permisionarios/{row.Id}/operadores>Operadores</a>";
            if (Perfiles.Puede(http.User, "Unidades.Consultar"))
                link += $"<a href=/permisionarios/{row.Id}/unidades>Unidad Vehicular</a>";
        }
        string attr = edit ? "" : "readonly";
        string Address(string label, string key, string value, string extra = "") =>
            edit ? $"<label>{label}<select name={key} data-value='{Vista.E(Mayusculas(value))}' {extra}></select></label>"
                : Vista.Campo(label, key, value, atributos: attr);
        string fields = $"""
            <div class=grid>
            {Vista.Campo("Número económico", "economico", row.Economico, max: 10, atributos: $"required pattern='[-A-Za-z0-9]{{1,10}}' {attr}")}
            {Vista.Campo("Nombre", "nombre", row.Nombre, max: 90, atributos: $"required {attr}")}
            {Vista.Campo("Apellido paterno", "paterno", row.Paterno, max: 90, atributos: attr)}
            {Vista.Campo("Apellido materno", "materno", row.Materno, max: 90, atributos: attr)}
            {(edit ? SelectorAgrupacion(Mayusculas(row.Agrupacion)) : Vista.Campo("Agrupación", "agrupacion", row.Agrupacion, atributos: attr))}
            {Vista.Campo("Fecha de nacimiento", "nacimiento", row.Nacimiento?.ToString("yyyy-MM-dd"), "date", atributos: attr)}
            <div>{Vista.Campo("RFC", "rfc", row.Rfc, max: 13, atributos: attr)}
            {(edit ? "<small id=rfc-sugerencia aria-live=polite></small><button id=rfc-usar class=quiet type=button hidden>Usar base sugerida</button>" : "")}</div>
            {Vista.Campo("Número de permiso", "permiso", row.Permiso, max: 50, atributos: attr)}
            {Vista.Campo("Vigencia del permiso", "vigencia", row.Vigencia?.ToString("yyyy-MM-dd"), "date", atributos: attr)}
            </div><h2 class=actions>Domicilio</h2><div class=grid>
            {Address("Estado", "estado", row.Estado)}
            {Address("Municipio o alcaldía", "municipio", row.Municipio)}
            {Address("Colonia", "colonia", row.Colonia)}
            {Address("Código postal", "cp", row.Cp)}
            {Vista.Campo("Calle", "calle", row.Calle, atributos: attr)}
            </div><h2 class=actions>Contacto y observaciones</h2><p class=hint>El teléfono y el correo son opcionales.</p><div class=grid>
            {Cuadricula("telefonos", "Teléfono", row.Telefonos, edit, true, phones)}
            {Cuadricula("correos", "Correo", row.Correos, edit, false, mails)}
            <label class=wide>Observaciones<textarea name=observaciones maxlength=1000 {attr}>{Vista.E(row.Observaciones)}</textarea></label></div>
            """;
        string end = edit && row.Id > 0
            ? $"<details class=panel><summary>Dar de baja</summary><form method=post action=/permisionarios/{row.Id}/baja>{Vista.Csrf(http)}<p>El expediente se conservará para consulta.</p><button>Confirmar baja</button></form></details>"
            : "";
        string content = edit
            ? $"<form id=ficha class=\"panel datos-padron\" data-captura method=post action=/permisionarios/{row.Id}>{Vista.Csrf(http)}<input type=hidden name=contactos_en_filas value=1>{fields}<div class=actions><button>Guardar</button>{link}</div></form><script src=/domicilio.js defer></script><script src=/permisionario.js defer></script>"
            : $"<section class=\"panel datos-padron\">{fields}<div class=actions>{link}</div></section>";
        string photo = row.Id > 0
            ? Fotografias.Panel(http, $"/permisionarios/{row.Id}/foto", edit, row.Id)
            : "<section class=panel><h2>Fotografía</h2><p>Guarda primero la ficha para cargar una imagen.</p></section>";
        string subtitle = row.Id == 0 ? "Nuevo permisionario" : $"Ficha {Mayusculas(row.Economico)}";
        return Vista.Pagina(http, "PERMISIONARIO",
            $"<p><a href=/permisionarios>Volver al catálogo</a></p><h2>{Vista.E(subtitle)}</h2>{(row.Baja is null ? "" : "<p class=alert>Registro dado de baja.</p>")}<p class=alert role=alert>{Vista.E(error)}</p><div class=ficha-layout>{content}<aside>{photo}</aside></div>{end}", error.Length > 0 ? 400 : 200);
    }
}
