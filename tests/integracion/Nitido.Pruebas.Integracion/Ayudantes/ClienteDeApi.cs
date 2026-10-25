using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nitido.Compartido.Api.Seguridad;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Cliente HTTP de las pruebas de autenticación: guarda las cookies a mano (así se pueden leer sus atributos),
/// habla por <c>https</c> (las cookies de sesión son <c>Secure</c>) y manda solo el encabezado de antifalsificación
/// cuando la cookie <c>XSRF-TOKEN</c> ya llegó, igual que <c>HttpClient</c> de Angular.
/// </summary>
internal sealed class ClienteDeApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ultimoSetCookie = new(StringComparer.Ordinal);

    public ClienteDeApi(WebApplicationFactory<Program> fabrica)
    {
        _http = fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    /// <summary>El último <c>Set-Cookie</c> completo recibido para esa cookie (con sus atributos), o <c>null</c>.</summary>
    public string? SetCookie(string nombre) => _ultimoSetCookie.GetValueOrDefault(nombre);

    public bool TieneCookie(string nombre) => _cookies.ContainsKey(nombre);

    /// <summary>Olvida una cookie, como si el navegador la hubiera borrado.</summary>
    public void OlvidarCookie(string nombre) => _cookies.Remove(nombre);

    /// <summary>Hace una petición <c>GET</c> a /yo para recibir la cookie de antifalsificación, si todavía no llegó.</summary>
    public async Task PrepararAsync(CancellationToken token)
    {
        if (!_cookies.ContainsKey(SeguridadWeb.CookieDeAntifalsificacion))
        {
            using var _ = await GetAsync("/api/autenticacion/yo", token);
        }
    }

    public Task<HttpResponseMessage> GetAsync(string ruta, CancellationToken token) =>
        EnviarAsync(HttpMethod.Get, ruta, null, true, token);

    public Task<HttpResponseMessage> PostAsync(string ruta, object? cuerpo, CancellationToken token, bool conAntifalsificacion = true) =>
        EnviarAsync(HttpMethod.Post, ruta, cuerpo ?? new { }, conAntifalsificacion, token);

    public async Task<HttpResponseMessage> EnviarAsync(
        HttpMethod metodo,
        string ruta,
        object? cuerpo,
        bool conAntifalsificacion,
        CancellationToken token)
    {
        using var peticion = new HttpRequestMessage(metodo, ruta);
        if (_cookies.Count > 0)
        {
            peticion.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        }

        if (conAntifalsificacion && metodo != HttpMethod.Get && _cookies.TryGetValue(SeguridadWeb.CookieDeAntifalsificacion, out var xsrf))
        {
            peticion.Headers.Add(SeguridadWeb.EncabezadoDeAntifalsificacion, xsrf);
        }

        if (cuerpo is not null)
        {
            peticion.Content = JsonContent.Create(cuerpo);
        }

        var respuesta = await _http.SendAsync(peticion, token);
        GuardarCookies(respuesta);
        return respuesta;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Lee el cuerpo JSON de una respuesta.</summary>
    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken token)
    {
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(token));
        return documento.RootElement.Clone();
    }

    private void GuardarCookies(HttpResponseMessage respuesta)
    {
        if (!respuesta.Headers.TryGetValues("Set-Cookie", out var encabezados))
        {
            return;
        }

        foreach (var encabezado in encabezados)
        {
            var partes = encabezado.Split(';', StringSplitOptions.TrimEntries);
            var nombreYValor = partes[0].Split('=', 2);
            var nombre = nombreYValor[0];
            var valor = nombreYValor.Length > 1 ? nombreYValor[1] : string.Empty;
            _ultimoSetCookie[nombre] = encabezado;

            var vencida = partes.Skip(1).Any(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.TryParse(p["expires=".Length..], out var vence)
                && vence < DateTimeOffset.UtcNow.AddYears(-1));
            if (valor.Length == 0 || vencida)
            {
                _cookies.Remove(nombre);
            }
            else
            {
                _cookies[nombre] = valor;
            }
        }
    }
}
