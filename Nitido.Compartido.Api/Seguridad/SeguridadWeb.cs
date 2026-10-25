using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nitido.Compartido.Api.Seguridad;

/// <summary>
/// Seguridad web común de la API (F0-03a): cookies de sesión <c>HttpOnly</c> y <c>Secure</c> con <c>SameSite=Strict</c>
/// (sin tokens en el navegador), autorización con sesión completa como política por defecto, límite de peticiones por IP en la
/// autenticación, protección contra falsificación de peticiones (doble envío firmado) y Data Protection con el
/// llavero en una carpeta local. Los valores salen de la sección <c>Seguridad</c> de la configuración, leídos cuando
/// se usan.
/// </summary>
public static class SeguridadWeb
{
    public const string CookieDeAntifalsificacion = "XSRF-TOKEN";
    public const string EncabezadoDeAntifalsificacion = "X-XSRF-TOKEN";

    private const int MinutosDeInactividadPorDefecto = 30;
    private const int HorasMaximasPorDefecto = 12;
    private const int MinutosDeSesionParcial = 10;
    private const int PeticionesPorMinutoPorDefecto = 10;
    private const string RutaDeLaApi = "/api";

    public static IServiceCollection AgregarSeguridadWeb(this IServiceCollection servicios)
    {
        servicios.AddAuthentication(EsquemasDeSesion.Completa)
            .AddCookie(EsquemasDeSesion.Completa, ConfigurarEventos)
            .AddCookie(EsquemasDeSesion.Parcial, ConfigurarEventos);

        servicios.AddOptions<CookieAuthenticationOptions>(EsquemasDeSesion.Completa)
            .Configure<IConfiguration, TimeProvider>((opciones, configuracion, reloj) =>
            {
                ConfigurarCookie(opciones, "nitido.sesion", reloj);
                opciones.ExpireTimeSpan = TimeSpan.FromMinutes(
                    LeerEntero(configuracion, "Seguridad:Sesion:MinutosInactividad", MinutosDeInactividadPorDefecto));
                opciones.SlidingExpiration = true;

                // Vida máxima absoluta: aunque se use sin parar, la sesión vence a las N horas de haberse abierto y
                // hay que volver a ingresar (ASVS 3.3.2). Salir no invalida en el servidor una cookie ya copiada:
                // ver el ADR 0004.
                var horasMaximas = LeerEntero(configuracion, "Seguridad:Sesion:HorasMaximas", HorasMaximasPorDefecto);
                opciones.Events.OnValidatePrincipal = async contexto =>
                {
                    // La renovación deslizante reinicia IssuedUtc; la hora de apertura real va en las propiedades de la cookie.
                    var abierta = contexto.Properties.Items.TryGetValue(SesionWeb.PropiedadDeApertura, out var texto)
                        && DateTimeOffset.TryParse(texto, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var valor)
                            ? valor
                            : (DateTimeOffset?)null;
                    if (abierta is null || reloj.GetUtcNow() - abierta.Value > TimeSpan.FromHours(horasMaximas))
                    {
                        contexto.RejectPrincipal();
                        await contexto.HttpContext.SignOutAsync(EsquemasDeSesion.Completa);
                    }
                };
            });
        servicios.AddOptions<CookieAuthenticationOptions>(EsquemasDeSesion.Parcial)
            .Configure<TimeProvider>((opciones, reloj) =>
            {
                ConfigurarCookie(opciones, "nitido.parcial", reloj);
                opciones.ExpireTimeSpan = TimeSpan.FromMinutes(MinutosDeSesionParcial);
                opciones.SlidingExpiration = false;
            });

        // La política por defecto (la que usa RequireAuthorization() sin argumentos) exige sesión completa. No hay política
        // de respaldo: con ella, hasta una ruta inexistente respondería 401 en vez de 404. En su lugar, cada grupo de rutas
        // llama a RequireAuthorization(), y la prueba Endpoints_ExigenSesionSalvoListaPermitida recorre todos los endpoints
        // y falla si alguno fuera de /salud, el ingreso y el registro queda sin exigir sesión.
        var porDefecto = new AuthorizationPolicyBuilder(EsquemasDeSesion.Completa).RequireAuthenticatedUser().Build();
        servicios.AddAuthorizationBuilder()
            .SetDefaultPolicy(porDefecto)
            .AddPolicy(
                EsquemasDeSesion.PoliticaDeSesionParcial,
                politica => politica.AddAuthenticationSchemes(EsquemasDeSesion.Parcial).RequireAuthenticatedUser());

        servicios.AddRateLimiter(_ => { });
        servicios.AddOptions<RateLimiterOptions>()
            .Configure<IConfiguration>((opciones, configuracion) =>
            {
                opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                opciones.AddPolicy(
                    EsquemasDeSesion.PoliticaDeLimite,
                    contexto => RateLimitPartition.GetFixedWindowLimiter(
                        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = LeerEntero(configuracion, "Seguridad:LimitePeticiones:PorMinuto", PeticionesPorMinutoPorDefecto),
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }));
                opciones.OnRejected = async (contexto, cancelacion) =>
                {
                    contexto.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await contexto.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
                        new ProblemDetailsContext
                        {
                            HttpContext = contexto.HttpContext,
                            ProblemDetails = new ProblemDetails
                            {
                                Status = StatusCodes.Status429TooManyRequests,
                                Title = "Hiciste demasiados intentos seguidos. Espera un minuto e inténtalo de nuevo.",
                            },
                        });
                };
            });

        // Llavero de Data Protection en una carpeta local (en .gitignore). Dónde vive en la nube se decide con el despliegue.
        servicios.AddDataProtection().SetApplicationName("nitido");
        servicios.AddOptions<KeyManagementOptions>()
            .Configure<IConfiguration, IHostEnvironment, ILoggerFactory>((opciones, configuracion, entorno, fabricaDeRegistros) =>
            {
                var carpeta = configuracion["Seguridad:Llavero:Directorio"];
                if (string.IsNullOrWhiteSpace(carpeta))
                {
                    carpeta = Path.Combine(entorno.ContentRootPath, ".llavero");
                }

                opciones.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(carpeta), fabricaDeRegistros);
            });

        return servicios;
    }

    /// <summary>
    /// Autenticación, límite de peticiones y autorización, en ese orden. La protección contra falsificación va antes
    /// (<see cref="UsarProteccionContraFalsificacion"/>).
    /// </summary>
    public static IApplicationBuilder UsarSeguridadWeb(this IApplicationBuilder aplicacion)
    {
        ArgumentNullException.ThrowIfNull(aplicacion);

        aplicacion.UseAuthentication();
        aplicacion.UseRateLimiter();
        aplicacion.UseAuthorization();
        return aplicacion;
    }

    /// <summary>
    /// Doble envío firmado (OWASP): toda respuesta de <c>/api</c> deja la cookie legible <c>XSRF-TOKEN</c>, y todo
    /// <c>POST</c>, <c>PUT</c>, <c>PATCH</c> o <c>DELETE</c> debe traer el mismo valor en <c>X-XSRF-TOKEN</c>
    /// (<c>HttpClient</c> de Angular lo hace solo). El valor va firmado con Data Protection, así que ni una cookie
    /// inyectada desde otro sitio sirve; y como no depende de la identidad, funciona antes y después de ingresar.
    /// Sin el encabezado, responde <c>400</c>.
    /// </summary>
    public static IApplicationBuilder UsarProteccionContraFalsificacion(this IApplicationBuilder aplicacion)
    {
        ArgumentNullException.ThrowIfNull(aplicacion);

        return aplicacion.Use(async (contexto, siguiente) =>
        {
            if (!contexto.Request.Path.StartsWithSegments(RutaDeLaApi))
            {
                await siguiente();
                return;
            }

            var protector = contexto.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("nitido.xsrf");
            var enCookie = contexto.Request.Cookies[CookieDeAntifalsificacion];
            var cookieValida = TryDesproteger(protector, enCookie);
            if (!cookieValida)
            {
                contexto.Response.OnStarting(() =>
                {
                    contexto.Response.Cookies.Append(
                        CookieDeAntifalsificacion,
                        protector.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                        new CookieOptions { HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
                    return Task.CompletedTask;
                });
            }

            if (EsEscritura(contexto.Request.Method)
                && !(cookieValida && EncabezadoCoincide(contexto.Request.Headers[EncabezadoDeAntifalsificacion].ToString(), enCookie!)))
            {
                contexto.Response.StatusCode = StatusCodes.Status400BadRequest;
                await contexto.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = contexto,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status400BadRequest,
                            Title = "No pudimos comprobar que la solicitud viene de nítido. Recarga la página e inténtalo de nuevo.",
                            Extensions = { ["codigo"] = "antifalsificacion" },
                        },
                    });
                return;
            }

            await siguiente();
        });
    }

    private static void ConfigurarEventos(CookieAuthenticationOptions opciones)
    {
        // Sin sesión se responde 401 (y la respuesta la completa ProblemDetails), nunca una redirección 302.
        opciones.Events.OnRedirectToLogin = contexto =>
        {
            contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        opciones.Events.OnRedirectToAccessDenied = contexto =>
        {
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void ConfigurarCookie(CookieAuthenticationOptions opciones, string nombre, TimeProvider reloj)
    {
        opciones.TimeProvider = reloj;
        opciones.Cookie.Name = nombre;
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        opciones.Cookie.SameSite = SameSiteMode.Strict;
        opciones.Cookie.Path = "/";
    }

    private static bool EsEscritura(string metodo) =>
        HttpMethods.IsPost(metodo) || HttpMethods.IsPut(metodo) || HttpMethods.IsPatch(metodo) || HttpMethods.IsDelete(metodo);

    private static bool TryDesproteger(IDataProtector protector, string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return false;
        }

        try
        {
            protector.Unprotect(valor);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool EncabezadoCoincide(string encabezado, string cookie) =>
        encabezado.Length > 0
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(encabezado), Encoding.UTF8.GetBytes(cookie));

    private static int LeerEntero(IConfiguration configuracion, string clave, int valorPorDefecto) =>
        int.TryParse(configuracion[clave], out var valor) && valor > 0 ? valor : valorPorDefecto;
}
