using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Reportes
{
    private const int Pagina = 50;
    private static readonly (string Key, string Label, string Permission)[] Modulos =
    [
        ("permisionario", "Permisionarios", "Permisionarios.Consultar"),
        ("taxistas", "Operadores", "Taxistas.Consultar"),
        ("unidades", "Unidad Vehicular", "Unidades.Consultar")
    ];

    private sealed class Fila
    {
        public string Tipo { get; init; } = "";
        public long Id { get; init; }
        public long PermisionarioId { get; init; }
        public string Economico { get; init; } = "";
        public string Agrupacion { get; init; } = "";
        public string Titular { get; init; } = "";
        public string Registro { get; init; } = "";
        public string Referencia { get; init; } = "";
        public DateOnly? Alta { get; init; }
        public DateOnly? Baja { get; init; }
    }

    public static void Mapear(WebApplication app) =>
        app.MapGet("/reportes", Mostrar).RequireAuthorization("Reportes.Consultar");

    private static async Task<IResult> Mostrar(HttpContext http, RegistroDb db, CancellationToken token)
    {
        var query = http.Request.Query;
        string Leer(string key) => query[key].ToString().Trim();
        string economic = Leer("economico"), group = Leer("agrupacion"), status = Leer("estado"), module = Leer("modulo");
        string altaDesde = Leer("altaDesde"), altaHasta = Leer("altaHasta"), bajaDesde = Leer("bajaDesde"), bajaHasta = Leer("bajaHasta");
        var allowed = Modulos.Where(item => Perfiles.Puede(http.User, item.Permission)).ToArray();
        if (allowed.Length == 0 || (module.Length != 0 && !allowed.Any(item => item.Key == module)))
            return Results.Forbid();
        var types = allowed.Where(item => module.Length == 0 || item.Key == module).Select(item => item.Key).ToArray();

        bool error = economic.Length > 10 || group.Length > 150 || status is not ("" or "activos" or "bajas");
        DateOnly? Fecha(string field)
        {
            if (field.Length == 0) return null;
            if (DateOnly.TryParseExact(field, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            error = true;
            return null;
        }
        var firstAlta = Fecha(altaDesde);
        var lastAlta = Fecha(altaHasta);
        var firstBaja = Fecha(bajaDesde);
        var lastBaja = Fecha(bajaHasta);
        error |= firstAlta > lastAlta || firstBaja > lastBaja;
        int page = 1;
        string rawPage = Leer("pagina");
        if (rawPage.Length > 0 && (!int.TryParse(rawPage, out page) || page is < 1 or > (int.MaxValue / Pagina)))
            error = true;

        var availableGroups = (await db.Permisionarios.Select(row => row.Agrupacion).Distinct().ToListAsync(token))
            .Select(Permisionarios.Mayusculas).Concat(Permisionarios.Agrupaciones).Where(name => name.Length > 0)
            .Distinct().Order(StringComparer.Create(CultureInfo.GetCultureInfo("es-MX"), false));
        var rows = new List<Fila>();
        if (!error)
        {
            const string datePattern = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$";
            var data = db.Database.SqlQuery<Fila>($"""
                WITH altas AS (
                    SELECT "PermisionarioId", "Tipo", "RegistroId", min("Fecha") AS fecha
                    FROM "Historial"
                    WHERE "Accion" = 'Alta' AND "Tipo" = ANY({types})
                    GROUP BY "PermisionarioId", "Tipo", "RegistroId"
                )
                SELECT 'permisionario'::text AS "Tipo", p."Id", p."Id" AS "PermisionarioId",
                    p."Economico", p."Agrupacion", concat_ws(' ', p."Nombre", p."Paterno", p."Materno") AS "Titular",
                    concat_ws(' ', p."Nombre", p."Paterno", p."Materno") AS "Registro", p."Permiso" AS "Referencia",
                    (h.fecha AT TIME ZONE 'America/Mexico_City')::date AS "Alta",
                    (p."Baja" AT TIME ZONE 'America/Mexico_City')::date AS "Baja"
                FROM "Permisionarios" p
                LEFT JOIN altas h ON h."PermisionarioId" = p."Id" AND h."Tipo" = 'permisionario' AND h."RegistroId" = 0
                WHERE 'permisionario' = ANY({types})
                UNION ALL
                SELECT a."Tipo", a."Id", p."Id", p."Economico", p."Agrupacion",
                    concat_ws(' ', p."Nombre", p."Paterno", p."Materno"),
                    CASE WHEN a."Tipo" = 'taxistas'
                        THEN concat_ws(' ', a."Nombre", a."Detalles"->>'paterno', a."Detalles"->>'materno')
                        ELSE concat_ws(' ', a."Detalles"->>'marca', a."Nombre") END,
                    a."Referencia", COALESCE(
                        CASE WHEN a."Tipo" = 'taxistas' AND (a."Detalles"->>'alta') ~ {datePattern}
                             AND pg_input_is_valid(a."Detalles"->>'alta', 'date') THEN (a."Detalles"->>'alta')::date END,
                        (h.fecha AT TIME ZONE 'America/Mexico_City')::date),
                    (a."Baja" AT TIME ZONE 'America/Mexico_City')::date
                FROM "Asignaciones" a
                JOIN "Permisionarios" p ON p."Id" = a."PermisionarioId"
                LEFT JOIN altas h ON h."PermisionarioId" = p."Id" AND h."Tipo" = a."Tipo" AND h."RegistroId" = a."Id"
                WHERE a."Tipo" = ANY({types})
                """);
            if (economic.Length > 0)
            {
                string pattern = "%" + economic.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                data = data.Where(row => EF.Functions.ILike(row.Economico, pattern, "\\"));
            }
            if (group.Length > 0)
            {
                string normalized = Permisionarios.Mayusculas(group);
                data = data.Where(row => row.Agrupacion.Trim().ToUpper() == normalized);
            }
            if (status == "activos") data = data.Where(row => row.Baja == null);
            if (status == "bajas") data = data.Where(row => row.Baja != null);
            if (firstAlta is DateOnly a1) data = data.Where(row => row.Alta >= a1);
            if (lastAlta is DateOnly a2) data = data.Where(row => row.Alta <= a2);
            if (firstBaja is DateOnly b1) data = data.Where(row => row.Baja >= b1);
            if (lastBaja is DateOnly b2) data = data.Where(row => row.Baja <= b2);
            rows = await data.OrderBy(row => row.Economico).ThenBy(row => row.Tipo).ThenBy(row => row.Id)
                .Skip((page - 1) * Pagina).Take(Pagina + 1).ToListAsync(token);
        }

        string Option(string value, string label, string selected) =>
            $"<option value='{Vista.E(value)}' {(value == selected ? "selected" : "")}>{Vista.E(label)}</option>";
        string FechaVisible(DateOnly? date, string fallback) =>
            date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? fallback;
        string Resultados()
        {
            if (rows.Count == 0) return "<p>Sin coincidencias.</p>";
            var body = string.Concat(rows.Take(Pagina).Select(row =>
            {
                string basePath = $"/permisionarios/{row.PermisionarioId}";
                string link = row.Tipo switch
                {
                    "taxistas" => $"{basePath}/operadores/{row.Id}",
                    "unidades" => $"{basePath}/unidades/{row.Id}",
                    _ => basePath
                };
                string kind = Modulos.First(item => item.Key == row.Tipo).Label;
                return $"""
                    <tr><td>{Vista.E(row.Economico)}</td><td>{Vista.E(row.Agrupacion)}</td><td>{Vista.E(row.Titular.Trim())}</td>
                    <td><small>{kind}</small><a href='{link}'>{Vista.E(row.Registro.Trim())}</a>
                    <small>{Vista.E(row.Referencia)}</small></td><td>{FechaVisible(row.Alta, "Sin fecha registrada")}</td>
                    <td>{FechaVisible(row.Baja, "—")}</td><td>{(row.Baja is null ? "Activo" : "Baja")}</td></tr>
                    """;
            }));
            return $"""
                <div class=report-table><table><thead><tr><th>Económico</th><th>Agrupación</th><th>Permisionario</th>
                <th>Registro</th><th>Alta</th><th>Baja</th><th>Estado</th></tr></thead><tbody>{body}</tbody></table></div>
                """;
        }
        string Ir(int target, string label) => $"""
            <a href='{Vista.E(QueryHelpers.AddQueryString("/reportes", new Dictionary<string, string?>
            {
                ["economico"] = economic, ["agrupacion"] = group, ["estado"] = status, ["modulo"] = module,
                ["altaDesde"] = altaDesde, ["altaHasta"] = altaHasta, ["bajaDesde"] = bajaDesde, ["bajaHasta"] = bajaHasta,
                ["pagina"] = target.ToString(CultureInfo.InvariantCulture)
            }))}'>{label}</a>
            """;
        string pagination = page > 1 || rows.Count > Pagina ? $"""
            <nav class=actions aria-label="Páginas del reporte">
            {(page > 1 ? Ir(page - 1, "Anterior") : "")}<span>Página {page}</span>
            {(rows.Count > Pagina ? Ir(page + 1, "Siguiente") : "")}</nav>
            """ : "";
        return Vista.Pagina(http, "Reportes", $"""
            <form class=panel method=get action=/reportes>
            <div class=grid>
            {Vista.Campo("Número económico", "economico", economic, max: 10)}
            <label>Agrupación<select name=agrupacion>{Option("", "Todas", group)}{string.Concat(availableGroups.Select(name => Option(name, name, group)))}</select></label>
            <label>Módulo<select name=modulo>{Option("", "Todos", module)}{string.Concat(allowed.Select(item => Option(item.Key, item.Label, module)))}</select></label>
            <label>Estado<select name=estado>{Option("", "Todos", status)}{Option("activos", "Activos", status)}{Option("bajas", "Bajas", status)}</select></label>
            </div>
            <div class=grid report-ranges>
            <fieldset><legend>Fecha de alta</legend>{Vista.Campo("Desde", "altaDesde", altaDesde, "date")}{Vista.Campo("Hasta", "altaHasta", altaHasta, "date")}</fieldset>
            <fieldset><legend>Fecha de baja</legend>{Vista.Campo("Desde", "bajaDesde", bajaDesde, "date")}{Vista.Campo("Hasta", "bajaHasta", bajaHasta, "date")}</fieldset>
            </div>
            <div class=actions><button>Consultar</button><a href=/reportes>Limpiar filtros</a></div></form>
            {(error ? "<p class=alert role=alert>Revisa los filtros y asegúrate de que cada fecha «Desde» sea anterior o igual a «Hasta».</p>"
                : $"<section class=panel><h2>Resultados</h2>{Resultados()}{pagination}</section>")}
            """, error ? 400 : 200);
    }
}
