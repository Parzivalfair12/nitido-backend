using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Nitido.Compartido.Api.Multiempresa;

/// <summary>Rutas que llevan el cliente activo: <c>/api/clientes/{uuidCliente}/...</c>.</summary>
public static class RutasDelCliente
{
    /// <summary>
    /// Crea el grupo <c>/api/clientes/{uuidCliente}{ruta}</c> con el filtro del cliente activo ya puesto, la marca
    /// <see cref="ClienteActivoRequerido"/> y la respuesta <c>404</c>. Es la única forma de armar rutas de cliente.
    /// </summary>
    /// <remarks>
    /// El valor de ruta no lleva la restricción <c>:guid</c> a propósito: el filtro lo analiza y responde el mismo
    /// <c>404</c> (mismo cuerpo) para un valor que no es GUID, para un cliente que no existe, de otra firma o sin acceso.
    /// </remarks>
    public static RouteGroupBuilder MapearRutasDelCliente(this IEndpointRouteBuilder rutas, string ruta = "")
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/api/clientes/{uuidCliente}" + ruta);
        grupo.RequireAuthorization();
        grupo.AddEndpointFilter<FiltroClienteActivo>();
        grupo.WithMetadata(new ClienteActivoRequerido());
        grupo.ProducesProblem(StatusCodes.Status404NotFound);
        return grupo;
    }
}
