using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Api.Multiempresa;

namespace Nitido.Compartido.Api.Seguridad;

/// <summary>Esquemas de autenticación (cookies) y políticas de autorización de la API.</summary>
public static class EsquemasDeSesion
{
    /// <summary>Sesión completa: la clave y el segundo factor ya se verificaron. Cookie <c>nitido.sesion</c>.</summary>
    public const string Completa = "nitido.sesion";

    /// <summary>Sesión parcial: la clave es correcta pero falta el segundo factor. Cookie <c>nitido.parcial</c>.</summary>
    public const string Parcial = "nitido.parcial";

    /// <summary>Política de los endpoints que exigen la sesión parcial (inscripción y verificación del doble factor).</summary>
    public const string PoliticaDeSesionParcial = "SesionParcial";

    /// <summary>Política del límite de peticiones por minuto de <c>/api/autenticacion/*</c>.</summary>
    public const string PoliticaDeLimite = "autenticacion";
}

/// <summary>Abrir y cerrar las cookies de sesión. Las cookies solo llevan el usuario y la firma (los reclamos).</summary>
public static class SesionWeb
{
    /// <summary>Propiedad de la cookie con la hora en que se abrió la sesión completa (no cambia al renovarse).</summary>
    internal const string PropiedadDeApertura = "abierta_utc";

    public static async Task IniciarSesionParcialAsync(this HttpContext contexto, Guid uuidUsuarios, Guid uuidFirmas)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        await contexto.SignOutAsync(EsquemasDeSesion.Completa);
        var principal = Armar(EsquemasDeSesion.Parcial, uuidUsuarios, uuidFirmas);
        await contexto.SignInAsync(EsquemasDeSesion.Parcial, principal, new AuthenticationProperties { IsPersistent = false });
        contexto.User = principal;
    }

    public static async Task IniciarSesionCompletaAsync(this HttpContext contexto, Guid uuidUsuarios, Guid uuidFirmas)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        await contexto.SignOutAsync(EsquemasDeSesion.Parcial);
        var principal = Armar(EsquemasDeSesion.Completa, uuidUsuarios, uuidFirmas);
        var propiedades = new AuthenticationProperties { IsPersistent = false };
        propiedades.Items[PropiedadDeApertura] = contexto.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await contexto.SignInAsync(EsquemasDeSesion.Completa, principal, propiedades);
        contexto.User = principal;
    }

    public static async Task CerrarSesionAsync(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        await contexto.SignOutAsync(EsquemasDeSesion.Completa);
        await contexto.SignOutAsync(EsquemasDeSesion.Parcial);
        contexto.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static ClaimsPrincipal Armar(string esquema, Guid uuidUsuarios, Guid uuidFirmas) =>
        new(new ClaimsIdentity(
            [
                new Claim(ReclamosDeSesion.UuidUsuarios, uuidUsuarios.ToString()),
                new Claim(ReclamosDeSesion.UuidFirmas, uuidFirmas.ToString()),
            ],
            esquema));
}
