using System.Net;
using Microsoft.AspNetCore.Antiforgery;

namespace Transportacion;
public static class Vista
{
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
    public static string Csrf(HttpContext http)
    {
        var token = http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
        return $"<input type=hidden name='{E(token.FormFieldName)}' value='{E(token.RequestToken)}'>";
    }

    public static string Campo(string etiqueta, string nombre, string? valor = "", string tipo = "text", int max = 150, string atributos = "") =>
        $"<label>{E(etiqueta)}<input type='{E(tipo)}' name='{E(nombre)}' value='{E(valor)}' maxlength='{max}' {atributos}></label>";

    public static IResult Pagina(HttpContext http, string titulo, string contenido, int status = 200)
    {
        string navegacion = "";
        bool signedIn = http.User.Identity?.IsAuthenticated == true;
        if (signedIn)
        {
            string Link(string path, string label, bool current) =>
                $"<a href='{path}'{(current ? " aria-current='page'" : "")}>{label}</a>";
            var path = http.Request.Path;
            bool operators = path.StartsWithSegments("/operadores") ||
                path.Value?.Contains("/operadores", StringComparison.OrdinalIgnoreCase) == true ||
                path.Value?.Contains("/taxistas", StringComparison.OrdinalIgnoreCase) == true;
            bool vehicles = path.StartsWithSegments("/unidades") ||
                path.Value?.Contains("/unidades", StringComparison.OrdinalIgnoreCase) == true;
            string records = Perfiles.Puede(http.User, "Permisionarios.Consultar")
                ? Link("/permisionarios", "Permisionarios", path.StartsWithSegments("/permisionarios") && !operators && !vehicles) : "";
            string drivers = Perfiles.Puede(http.User, "Taxistas.Consultar")
                ? Link("/operadores", "Operadores", operators) : "";
            string units = Perfiles.Puede(http.User, "Unidades.Consultar")
                ? Link("/unidades", "Unidad Vehicular", vehicles) : "";
            string admin = http.User.IsInRole("Administrador")
                ? Link("/usuarios", "Usuarios", path.StartsWithSegments("/usuarios") || path.StartsWithSegments("/perfiles")) : "";
            navegacion = $"""
                <aside class=app-sidebar><nav aria-label="Menú principal">
                {records}{drivers}{units}{admin}
                <form method=post action=/salir>{Csrf(http)}<button class=quiet>Salir</button></form>
                </nav></aside>
                """;
        }
        return Results.Content($"""
            <!doctype html><html lang="es-MX"><head><meta charset=utf-8>
            <meta name=viewport content="width=device-width,initial-scale=1">
            <title>{E(titulo)} · Transportación Terrestre</title><link rel=stylesheet href=/app.css><script src=/captura.js defer></script></head>
            <body><header><div class=brand><span class=monogram>TT</span><div><strong>Transportación Terrestre</strong><small>Registro y expedientes</small></div></div></header>
            <div class='{(signedIn ? "app-layout" : "")}'>{navegacion}<main><h1>{E(titulo)}</h1>{contenido}</main></div></body></html>
            """, "text/html; charset=utf-8", statusCode: status);
    }
}
