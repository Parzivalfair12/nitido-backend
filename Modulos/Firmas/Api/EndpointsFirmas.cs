using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Nitido.Compartido.Api.Resultados;
using Nitido.Compartido.Api.Seguridad;
using Nitido.Firmas.Aplicacion.Autenticacion;
using Nitido.Firmas.Aplicacion.Registro;

namespace Nitido.Firmas.Api;

/// <summary>Cuerpo de <c>POST /api/autenticacion/ingreso</c>.</summary>
public sealed record SolicitudDeIngreso(string Correo, string Clave);

/// <summary>Cuerpo de los endpoints que reciben un código del segundo factor o de recuperación.</summary>
public sealed record SolicitudDeCodigo(string Codigo);

public sealed record RespuestaDeIngreso(string Paso);

public sealed record RespuestaDeSesion(string Nombre, string Correo, string Rol, string Firma);

public sealed record RespuestaDeInscripcion(string ClaveManual, string UriOtpauth);

public sealed record RespuestaDeCodigosDeRecuperacion(IReadOnlyList<string> CodigosRecuperacion);

public sealed record RespuestaDeSegundoFactor(string Paso);

public sealed record RespuestaDeRecuperacion(string Paso, int CodigosRestantes);

public sealed record RespuestaDeRegistro(Guid UuidFirma);

public static class EndpointsFirmas
{
    public const string PasoInscribirSegundoFactor = "inscribir-segundo-factor";
    public const string PasoSegundoFactor = "segundo-factor";
    public const string PasoCompleto = "completo";

    /// <summary>Rutas del módulo Firmas: registro de firma y autenticación (ingreso, sesión y doble factor).</summary>
    public static IEndpointRouteBuilder MapearFirmas(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var firmas = rutas.MapGroup("/api/firmas");
        firmas.MapPost("/registro", RegistrarAsync)
            .AllowAnonymous()
            .RequireRateLimiting(EsquemasDeSesion.PoliticaDeLimite)
            .AddEndpointFilter(SoloConRegistroHabilitado)
            .Produces<RespuestaDeRegistro>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // El límite de peticiones por IP protege lo que se puede adivinar (ingreso, códigos, registro). /yo y /salida no
        // lo llevan: una firma entera detrás de una sola IP recarga la aplicación varias veces por minuto.
        var autenticacion = rutas.MapGroup("/api/autenticacion");

        autenticacion.MapPost("/ingreso", IngresarAsync)
            .AllowAnonymous()
            .RequireRateLimiting(EsquemasDeSesion.PoliticaDeLimite)
            .Produces<RespuestaDeIngreso>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        autenticacion.MapGet("/yo", ObtenerSesionAsync)
            .RequireAuthorization()
            .Produces<RespuestaDeSesion>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        autenticacion.MapPost("/salida", SalirAsync)
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        var segundoFactor = autenticacion.MapGroup("/segundo-factor")
            .RequireAuthorization(EsquemasDeSesion.PoliticaDeSesionParcial)
            .RequireRateLimiting(EsquemasDeSesion.PoliticaDeLimite);

        segundoFactor.MapGet("/inscripcion", ObtenerInscripcionAsync)
            .Produces<RespuestaDeInscripcion>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        segundoFactor.MapPost("/inscripcion", ConfirmarInscripcionAsync)
            .Produces<RespuestaDeCodigosDeRecuperacion>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        segundoFactor.MapPost(string.Empty, VerificarAsync)
            .Produces<RespuestaDeSegundoFactor>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        segundoFactor.MapPost("/recuperacion", VerificarRecuperacionAsync)
            .Produces<RespuestaDeRecuperacion>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return rutas;
    }

    private static async ValueTask<object?> SoloConRegistroHabilitado(EndpointFilterInvocationContext contexto, EndpointFilterDelegate siguiente)
    {
        var configuracion = contexto.HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
        var habilitado = bool.TryParse(configuracion?["Registro:Habilitado"], out var valor) && valor;
        return habilitado ? await siguiente(contexto) : TypedResults.NotFound();
    }

    private static async Task<IResult> RegistrarAsync(
        RegistrarFirmaComando comando,
        RegistrarFirmaCasoDeUso casoDeUso,
        CancellationToken cancelacion)
    {
        var resultado = await casoDeUso.EjecutarAsync(comando, cancelacion);
        return resultado.ARespuestaHttp(uuid => TypedResults.Created($"/api/firmas/{uuid}", new RespuestaDeRegistro(uuid)));
    }

    private static async Task<IResult> IngresarAsync(
        SolicitudDeIngreso solicitud,
        IServicioDeAutenticacion servicio,
        HttpContext http,
        CancellationToken cancelacion)
    {
        var resultado = await servicio.IngresarAsync(solicitud.Correo, solicitud.Clave, cancelacion);
        if (resultado.EsError)
        {
            return resultado.Error.ARespuestaHttp();
        }

        var ingreso = resultado.Valor;
        await http.IniciarSesionParcialAsync(ingreso.Identidad.UuidUsuarios, ingreso.Identidad.UuidFirmas);
        return TypedResults.Ok(new RespuestaDeIngreso(
            ingreso.Paso == PasoDeIngreso.SegundoFactor ? PasoSegundoFactor : PasoInscribirSegundoFactor));
    }

    private static async Task<IResult> ObtenerSesionAsync(IServicioDeAutenticacion servicio, CancellationToken cancelacion)
    {
        var resultado = await servicio.ObtenerPerfilAsync(cancelacion);
        return resultado.ARespuestaHttp(p => TypedResults.Ok(new RespuestaDeSesion(p.Nombre, p.Correo, p.Rol, p.Firma)));
    }

    private static async Task<IResult> SalirAsync(HttpContext http, CancellationToken cancelacion)
    {
        cancelacion.ThrowIfCancellationRequested();
        await http.CerrarSesionAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ObtenerInscripcionAsync(IServicioDeAutenticacion servicio, CancellationToken cancelacion)
    {
        var resultado = await servicio.ObtenerInscripcionAsync(cancelacion);
        return resultado.ARespuestaHttp(i => TypedResults.Ok(new RespuestaDeInscripcion(i.ClaveManual, i.UriOtpauth)));
    }

    private static async Task<IResult> ConfirmarInscripcionAsync(
        SolicitudDeCodigo solicitud,
        IServicioDeAutenticacion servicio,
        HttpContext http,
        CancellationToken cancelacion)
    {
        var resultado = await servicio.ConfirmarInscripcionAsync(solicitud.Codigo, cancelacion);
        if (resultado.EsError)
        {
            return resultado.Error.ARespuestaHttp();
        }

        // Quien confirma su aplicación acaba de demostrar que la tiene: queda con sesión completa.
        await http.IniciarSesionCompletaAsync(resultado.Valor.Identidad.UuidUsuarios, resultado.Valor.Identidad.UuidFirmas);
        return TypedResults.Ok(new RespuestaDeCodigosDeRecuperacion(resultado.Valor.Codigos));
    }

    private static async Task<IResult> VerificarAsync(
        SolicitudDeCodigo solicitud,
        IServicioDeAutenticacion servicio,
        HttpContext http,
        CancellationToken cancelacion)
    {
        var resultado = await servicio.VerificarSegundoFactorAsync(solicitud.Codigo, cancelacion);
        if (resultado.EsError)
        {
            return resultado.Error.ARespuestaHttp();
        }

        await http.IniciarSesionCompletaAsync(resultado.Valor.UuidUsuarios, resultado.Valor.UuidFirmas);
        return TypedResults.Ok(new RespuestaDeSegundoFactor(PasoCompleto));
    }

    private static async Task<IResult> VerificarRecuperacionAsync(
        SolicitudDeCodigo solicitud,
        IServicioDeAutenticacion servicio,
        HttpContext http,
        CancellationToken cancelacion)
    {
        var resultado = await servicio.VerificarRecuperacionAsync(solicitud.Codigo, cancelacion);
        if (resultado.EsError)
        {
            return resultado.Error.ARespuestaHttp();
        }

        await http.IniciarSesionCompletaAsync(resultado.Valor.Identidad.UuidUsuarios, resultado.Valor.Identidad.UuidFirmas);
        return TypedResults.Ok(new RespuestaDeRecuperacion(PasoCompleto, resultado.Valor.CodigosRestantes));
    }
}
