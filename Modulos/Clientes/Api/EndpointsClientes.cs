using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nitido.Clientes.Aplicacion.Clientes;
using Nitido.Compartido.Api.Multiempresa;
using Nitido.Compartido.Api.Resultados;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Clientes.Api;

public static class EndpointsClientes
{
    /// <summary>
    /// Rutas del módulo Clientes: crear y listar en <c>/api/clientes</c>, y el detalle en
    /// <c>/api/clientes/{uuidCliente}</c>, que se arma con <c>MapearRutasDelCliente</c> (valida el acceso al cliente).
    /// </summary>
    public static IEndpointRouteBuilder MapearClientes(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var clientes = rutas.MapGroup("/api/clientes").RequireAuthorization();

        clientes.MapPost(string.Empty, CrearAsync)
            .Produces<ClienteDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        clientes.MapGet(string.Empty, ListarAsync)
            .Produces<IReadOnlyList<ClienteDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        rutas.MapearRutasDelCliente()
            .MapGet(string.Empty, ObtenerAsync)
            .Produces<ClienteDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return rutas;
    }

    private static async Task<IResult> CrearAsync(
        CrearClienteComando comando,
        CrearClienteCasoDeUso casoDeUso,
        CancellationToken cancelacion)
    {
        var resultado = await casoDeUso.EjecutarAsync(comando, cancelacion);
        return resultado.ARespuestaHttp(cliente => TypedResults.Created($"/api/clientes/{cliente.Uuid}", cliente));
    }

    private static async Task<IResult> ListarAsync(ListarClientesCasoDeUso casoDeUso, CancellationToken cancelacion) =>
        TypedResults.Ok(await casoDeUso.EjecutarAsync(cancelacion));

    // El cliente sale del contexto de sesión, no del valor de la ruta: solo llega ahí después de que el filtro de
    // MapearRutasDelCliente validó el acceso (y el valor de la ruta no se enlaza aquí, para que un valor que no es
    // GUID llegue al filtro y reciba su mismo 404).
    private static async Task<IResult> ObtenerAsync(
        IContextoSesion sesion,
        ObtenerClienteCasoDeUso casoDeUso,
        CancellationToken cancelacion)
    {
        var resultado = await casoDeUso.EjecutarAsync(sesion.UuidEmpresas ?? Guid.Empty, cancelacion);
        return resultado.ARespuestaHttp(cliente => TypedResults.Ok(cliente));
    }
}
