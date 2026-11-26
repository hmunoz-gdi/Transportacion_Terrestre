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
        if (http.User.Identity?.IsAuthenticated == true)
        {
            string admin = http.User.IsInRole("Administrador") ? "<a href=/usuarios>Usuarios</a>" : "";
            navegacion = $"<nav><a href=/permisionarios>Permisionarios</a>{admin}<form method=post action=/salir>{Csrf(http)}<button class=quiet>Salir</button></form></nav>";
        }
        return Results.Content($"""
            <!doctype html><html lang="es-MX"><head><meta charset=utf-8>
            <meta name=viewport content="width=device-width,initial-scale=1">
            <title>{E(titulo)} · Transportación Terrestre</title><link rel=stylesheet href=/app.css><script src=/captura.js defer></script></head>
            <body><header><div class=brand><span class=monogram>TT</span><div><strong>Transportación Terrestre</strong><small>Registro y expedientes</small></div></div>{navegacion}</header>
            <main><h1>{E(titulo)}</h1>{contenido}</main></body></html>
            """, "text/html; charset=utf-8", statusCode: status);
    }
}
