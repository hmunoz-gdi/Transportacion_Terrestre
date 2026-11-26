using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class Asignaciones
{
    public static void Mapear(WebApplication app)
    {
        foreach (string type in new[] { "taxistas", "unidades" })
        {
            string module = type == "taxistas" ? "Taxistas" : "Unidades";
            var group = app.MapGroup($"/permisionarios/{{id:long}}/{type}").RequireAuthorization(module + ".Consultar");
            group.MapGet("", (long id, HttpContext http, RegistroDb db) => Panel(id, type, http, db));
            group.MapPost("", (long id, HttpContext http, RegistroDb db) => Alta(id, type, http, db))
                .RequireAuthorization(module + ".Cambiar");
        }
    }

    private static async Task<IResult> Panel(long id, string type, HttpContext http, RegistroDb db)
    {
        var holder = await db.Permisionarios.FindAsync(id);
        if (holder is null) return Results.NotFound();
        var rows = await db.Asignaciones.Where(row => row.PermisionarioId == id && row.Tipo == type)
            .OrderBy(row => row.Nombre).ToListAsync();
        bool drivers = type == "taxistas";
        string list = string.Concat(rows.Select(row => $"""
            <article class=panel><strong>{Vista.E(row.Nombre)}</strong>
            <small>{(drivers ? "Licencia" : "Placa")} {Vista.E(row.Referencia)}{(row.Vigencia is DateOnly date ? $" · Vigencia {date:dd/MM/yyyy}" : "")}</small></article>
            """));
        string form = holder.Baja is null && Perfiles.Puede(http.User, drivers ? "Taxistas.Cambiar" : "Unidades.Cambiar") ?
            $"""
            <form method=post class=panel>{Vista.Csrf(http)}<h2>{(drivers ? "Registrar taxista" : "Registrar unidad")}</h2>
            <div class=grid>
            {Vista.Campo(drivers ? "Nombre completo" : "Marca y modelo", "nombre", max: 150, atributos: "required")}
            {Vista.Campo(drivers ? "Número de licencia" : "Placa", "referencia", max: 50, atributos: "required")}
            {Vista.Campo(drivers ? "Vigencia de licencia" : "Vigencia de tarjeta de circulación", "vigencia", tipo: "date")}
            </div><div class=actions><button>Guardar</button></div></form>
            """ : "";
        return Vista.Pagina(http, drivers ? "Taxistas" : "Unidades vehiculares",
            $"<p><a href=/permisionarios/{id}>Ficha {Vista.E(holder.Economico)}</a></p>{(rows.Count > 0 ? list : "<p>Sin registros asociados.</p>")}{form}");
    }

    private static async Task<IResult> Alta(long id, string type, HttpContext http, RegistroDb db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.BloquearFicha(id);
        var holder = await db.Permisionarios.FindAsync(id);
        if (holder is null) return Results.NotFound();
        if (holder.Baja is not null) return Results.Conflict("El expediente está dado de baja.");
        var form = await http.Request.ReadFormAsync();
        string name = form["nombre"].ToString().Trim(), reference = form["referencia"].ToString().Trim();
        string date = form["vigencia"].ToString();
        if (name.Length is 0 or > 150 || reference.Length is 0 or > 50 ||
            (date.Length > 0 && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            return Vista.Pagina(http, "Revisa los campos", $"<p>Indica nombre, referencia y una fecha válida.</p><a href=/permisionarios/{id}/{type}>Volver</a>", 400);
        DateOnly? validity = date.Length == 0 ? null : DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        db.Asignaciones.Add(new Asignacion { PermisionarioId = id, Tipo = type, Nombre = name, Referencia = reference, Vigencia = validity });
        Acceso.Registrar(db, http, $"Alta en {type}", $"{id} · {reference}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Redirect($"/permisionarios/{id}/{type}");
    }
}
