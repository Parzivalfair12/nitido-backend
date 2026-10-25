using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Api.Multiempresa;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Compartido.Api;

/// <summary>Registro de los servicios web comunes de la API.</summary>
public static class ApiNitido
{
    /// <summary>
    /// Registra el <see cref="IContextoSesion"/> del host (<c>ContextoSesionHttp</c>, por alcance) y el acceso a
    /// <c>HttpContext</c> que necesita. Se llama una vez, desde el host, antes de los módulos.
    /// </summary>
    public static IServiceCollection AgregarApiNitido(this IServiceCollection servicios)
    {
        servicios.AddHttpContextAccessor();
        servicios.AddScoped<ContextoDeAlcance>();
        servicios.AddScoped<IContextoSesion, ContextoSesionHttp>();
        return servicios;
    }
}
