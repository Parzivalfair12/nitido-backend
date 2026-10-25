using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Compartido.Api.Multiempresa;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Endpoints que existen solo en las pruebas de integración, nunca en el host (F0-02b, 6.2). Un
/// <see cref="IStartupFilter"/> (el mismo recurso que usa <c>ErroresPruebas</c>) que, después de la canalización
/// de la aplicación, monta <c>UseRouting</c> y <c>UseEndpoints</c> con dos rutas de cliente creadas con
/// <c>MapearRutasDelCliente</c>, para probar el filtro real con el <c>IAccesoAClientes</c> real, la base real y
/// las dos políticas:
/// <list type="bullet">
/// <item><c>GET /api/clientes/{uuidCliente:guid}/prueba-eco</c> responde <c>{ "uuidEmpresas": ... }</c> con
/// <see cref="IContextoSesion.UuidEmpresas"/>.</item>
/// <item><c>GET /api/clientes/{uuidCliente:guid}/prueba-sql</c> responde las tres llaves de
/// <c>SESSION_CONTEXT</c> que ve una conexión abierta por EF <b>después</b> de que el filtro validó el cliente.</item>
/// </list>
/// Para ejercitar la cadena real (filtro, <c>HttpContext.Items</c>, <c>ContextoSesionHttp</c>, interceptor) el host
/// debe usar su <see cref="IContextoSesion"/> de producción (<c>FabricaNitido(..., usarSesionDePrueba: false)</c>).
/// El usuario y la firma entran, como en producción, por los reclamos de <c>HttpContext.User</c> (un middleware de
/// la prueba los arma a partir de <paramref name="quienActua"/>, que la prueba cambia entre peticiones).
/// </summary>
/// <param name="quienActua">Quién hace la petición. Sin usuario o sin firma, la petición no lleva reclamos.</param>
/// <param name="conRutaSinFiltro">
/// Agrega además una ruta <c>/api/clientes/{uuidCliente:guid}/sin-filtro</c> armada a mano, sin
/// <c>MapearRutasDelCliente</c>: es el control negativo de <c>RutasDelCliente_TodasValidanElClienteActivo</c>.
/// </param>
internal sealed class EndpointsDePrueba(ContextoSesionDePrueba quienActua, bool conRutaSinFiltro = false) : IStartupFilter
{
    public const string RutaDelEco = "prueba-eco";
    public const string RutaDeSql = "prueba-sql";
    public const string RutaSinFiltro = "sin-filtro";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) =>
        aplicacion =>
        {
            siguiente(aplicacion);

            aplicacion.UseRouting();
            aplicacion.UseAuthorization();

            // Los reclamos entran después de la autorización: aun con AllowAnonymous, ese middleware reemplaza el
            // HttpContext.User por uno anónimo cuando ninguna cookie coincide, y en producción los reclamos salen
            // de la cookie de sesión, no de aquí.
            aplicacion.Use((contexto, continuar) =>
            {
                contexto.User = ArmarUsuario(quienActua);
                return continuar();
            });

            aplicacion.UseEndpoints(rutas =>
            {
                // La sesión de estas rutas la arma el middleware de arriba, no la cookie: se salta la política por defecto.
                var eco = rutas.MapearRutasDelCliente("/" + RutaDelEco);
                eco.AllowAnonymous();
                eco.MapGet("/", (IContextoSesion sesion) => TypedResults.Ok(new { uuidEmpresas = sesion.UuidEmpresas }));

                var sql = rutas.MapearRutasDelCliente("/" + RutaDeSql);
                sql.AllowAnonymous();
                sql.MapGet(
                    "/",
                    async (ClientesDbContext contexto, CancellationToken token) =>
                    {
                        var fila = await LecturaDeSesionSql.LeerAsync(contexto, token);
                        return TypedResults.Ok(new { uuidUsuarios = fila.UuidUsuarios, uuidFirmas = fila.UuidFirmas, uuidEmpresas = fila.UuidEmpresas });
                    });

                if (conRutaSinFiltro)
                {
                    rutas.MapGet("/api/clientes/{uuidCliente:guid}/" + RutaSinFiltro, () => TypedResults.Ok("sin filtro")).AllowAnonymous();
                }
            });
        };

    private static ClaimsPrincipal ArmarUsuario(ContextoSesionDePrueba quien)
    {
        var identidad = new ClaimsIdentity("prueba");
        if (quien.UuidUsuarios != Guid.Empty)
        {
            identidad.AddClaim(new Claim(ReclamosDeSesion.UuidUsuarios, quien.UuidUsuarios.ToString()));
        }

        if (quien.UuidFirmas != Guid.Empty)
        {
            identidad.AddClaim(new Claim(ReclamosDeSesion.UuidFirmas, quien.UuidFirmas.ToString()));
        }

        return new ClaimsPrincipal(identidad);
    }
}
