namespace Transportacion;

public static class Inicio
{
    internal record Modulo(string Ruta, string Nombre, string Descripcion, string? Permiso);

    private static readonly Modulo[] Modulos =
    [
        new("/permisionarios", "Permisionarios", "Consulta fichas, documentos, carátulas e historial.", "Permisionarios.Consultar"),
        new("/operadores", "Operadores", "Busca operadores y consulta su vínculo con el permisionario.", "Taxistas.Consultar"),
        new("/unidades", "Unidad Vehicular", "Consulta unidades, placas y datos del vehículo.", "Unidades.Consultar"),
        new("/reportes", "Reportes", "Filtra altas y bajas y exporta los resultados a Excel.", "Reportes.Consultar"),
        new("/usuarios", "Usuarios y perfiles", "Administra cuentas, perfiles y permisos.", null)
    ];

    internal static IEnumerable<Modulo> Visibles(HttpContext http) =>
        Modulos.Where(module => module.Permiso is string permission
            ? Perfiles.Puede(http.User, permission)
            : http.User.IsInRole("Administrador"));

    public static void Mapear(WebApplication app) =>
        app.MapGet("/", Mostrar).RequireAuthorization();

    private static IResult Mostrar(HttpContext http)
    {
        var visible = Visibles(http).ToArray();
        string content = visible.Length == 0
            ? "<p>Tu cuenta todavía no tiene módulos asignados.</p>"
            : "<div class=modulos-inicio>" + string.Concat(visible.Select(module => $"""
                <a class=modulo-inicio href='{module.Ruta}'>
                <h2>{Vista.E(module.Nombre)}</h2><p>{Vista.E(module.Descripcion)}</p></a>
                """)) + "</div>";
        return Vista.Pagina(http, "Inicio", content);
    }
}
