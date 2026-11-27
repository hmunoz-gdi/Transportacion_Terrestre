using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Transportacion;

public static class HistorialFichas
{
    public const string Titular = "permisionario";

    public static void Guardar<T>(RegistroDb db, HttpContext http, long id, string tipo, long registro,
        string accion, T datos) where T : class =>
        db.Historial.Add(Nueva(http, id, tipo, registro, accion, JsonSerializer.Serialize(datos)));

    public static VersionFicha Nueva(HttpContext http, long id, string tipo, long registro, string accion, string? datos = null) =>
        new()
        {
            PermisionarioId = id, Tipo = tipo, RegistroId = registro, Accion = accion,
            Usuario = http.User.Identity?.Name ?? "", Datos = datos
        };

    public static async Task<IResult> Ver(HttpContext http, RegistroDb db, long id, string tipo, long registro,
        string ruta, long? antes)
    {
        if (!await db.Permisionarios.AnyAsync(row => row.Id == id) ||
            (registro != 0 && !await db.Asignaciones.AnyAsync(row =>
                row.Id == registro && row.PermisionarioId == id && row.Tipo == tipo)))
            return Results.NotFound();

        var entries = await db.Historial.AsNoTracking()
            .Where(row => row.PermisionarioId == id && row.Tipo == tipo && row.RegistroId == registro &&
                (antes == null || row.Id < antes))
            .OrderByDescending(row => row.Id).Take(26).ToListAsync();
        string versions = string.Concat(entries.Take(25).Select(row =>
        {
            string detail = row.Foto is Guid ? $"<a href='{ruta}/foto?version={row.Id}' target=_blank rel=noopener>Ver fotografía</a>"
                : Datos(row.Datos);
            return $"""
                <details class=panel><summary>{Vista.E(row.Accion)} · {row.Fecha:dd/MM/yyyy HH:mm} UTC · {Vista.E(row.Usuario)}</summary>
                {detail}</details>
                """;
        }));
        string next = entries.Count > 25 ? $"<p><a href='{ruta}/historial?antes={entries[24].Id}'>Ver versiones anteriores</a></p>" : "";
        return Vista.Pagina(http, "Historial de la ficha", $"""
            <p><a href='{ruta}'>Volver a la ficha</a></p>
            {(entries.Count == 0 ? "<p>Sin versiones registradas.</p>" : versions)}
            {next}
            """);
    }

    private static string Datos(string? json)
    {
        if (json is null) return "";
        using var document = JsonDocument.Parse(json);
        string Fields(JsonElement element) => string.Concat(element.EnumerateObject().Select(property =>
        {
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.Object) return Fields(value);
            if (property.Name == "Detalles" && value.ValueKind == JsonValueKind.String)
            {
                using var details = JsonDocument.Parse(value.GetString()!);
                return Fields(details.RootElement);
            }
            if (property.Name is "Id" or "PermisionarioId" or "Tipo") return "";
            string label = property.Name switch
            {
                "Alta" or "alta" => "Fecha de alta", "Baja" => "Fecha de baja", "Nacimiento" => "Fecha de nacimiento", "Cp" or "cp" => "Código postal",
                "Paterno" or "paterno" => "Apellido paterno", "Materno" or "materno" => "Apellido materno",
                "Economico" => "Número económico", "Rfc" => "RFC", "Referencia" => "Licencia o placa",
                _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                    System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z])([A-Z])", "$1 $2"))
            };
            string rendered = value.ValueKind == JsonValueKind.Null || value.ToString().Length == 0 ? "—" : value.ToString();
            return $"<dt>{Vista.E(label)}</dt><dd>{Vista.E(rendered)}</dd>";
        }));
        return $"<dl class=history-data>{Fields(document.RootElement)}</dl>";
    }
}
