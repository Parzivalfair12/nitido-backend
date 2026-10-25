using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Compartido.Api.Multiempresa;

/// <summary>
/// Filtro de endpoint de las rutas <c>/api/clientes/{uuidCliente}/...</c>. Deja pasar la petición solo si el
/// usuario de la sesión puede entrar al cliente; en cualquier otro caso responde el mismo <c>404</c>, con el
/// mismo cuerpo, para no revelar si el cliente existe: el valor no es un GUID, el cliente no existe, es de otra
/// firma, el usuario no tiene acceso o no hay sesión. Nunca responde <c>403</c>.
/// </summary>
internal sealed class FiltroClienteActivo : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var http = contexto.HttpContext;

        if (!http.Request.RouteValues.TryGetValue("uuidCliente", out var crudo) || !Guid.TryParse(crudo?.ToString(), out var cliente))
        {
            return NoEncontrado();
        }

        var sesion = http.RequestServices.GetRequiredService<IContextoSesion>();
        if (sesion.UuidUsuarios == Guid.Empty || sesion.UuidFirmas == Guid.Empty)
        {
            return NoEncontrado();
        }

        var acceso = http.RequestServices.GetRequiredService<IAccesoAClientes>();
        if (!await acceso.PuedeEntrarAsync(cliente, http.RequestAborted))
        {
            return NoEncontrado();
        }

        // Solo desde aquí el cliente llega a IContextoSesion.UuidEmpresas, al filtro de EF y al SESSION_CONTEXT.
        http.Items[ClienteActivo.ClaveValidada] = cliente;
        return await siguiente(contexto);
    }

    private static IResult NoEncontrado() =>
        TypedResults.Problem(
            title: "No encontramos ese cliente.",
            statusCode: StatusCodes.Status404NotFound,
            extensions: new Dictionary<string, object?> { ["codigo"] = "cliente_no_encontrado" });
}
