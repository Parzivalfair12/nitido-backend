using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Nitido.Api.Configuracion;
using Nitido.Clientes.Api;
using Nitido.Clientes.Infraestructura;
using Nitido.Compartido.Api;
using Nitido.Compartido.Api.Seguridad;
using Nitido.Compartido.Infraestructura;
using Nitido.Firmas.Api;
using Nitido.Firmas.Infraestructura;
using Serilog;
using Serilog.Events;

// Registrador inicial: escribe en la consola mientras se arma el host. Lee la misma configuración
// de Serilog que el host (appsettings.json, appsettings.{entorno}.json y variables de entorno),
// así que respeta el formato del entorno: JSON compacto fuera de Development y texto legible en Development.
var entorno = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environments.Production;
var configuracionInicial = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{entorno}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuracionInicial)
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

// Títulos en español (regla 8) para los errores que ASP.NET Core responde con su texto en inglés.
var TitulosEnEspanol = new Dictionary<int, string>
{
    [StatusCodes.Status400BadRequest] = "La solicitud no es válida. Revisa los datos e inténtalo de nuevo.",
    [StatusCodes.Status404NotFound] = "No encontramos lo que buscas.",
    [StatusCodes.Status405MethodNotAllowed] = "Esa acción no está permitida en esta dirección.",
    [StatusCodes.Status415UnsupportedMediaType] = "El formato de la solicitud no es compatible.",
};

try
{
    var builder = WebApplication.CreateBuilder(args);

    // preserveStaticLogger: true evita congelar el registrador estático, de modo que varios hosts
    // en el mismo proceso (pruebas de integración) puedan arrancar sin el error «already frozen».
    builder.Services.AddSerilog(
        (servicios, configuracion) => configuracion
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(servicios)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    // Toda respuesta de error usa ProblemDetails (RFC 9457).
    builder.Services.AddProblemDetails(opciones =>
        opciones.CustomizeProblemDetails = contexto =>
        {
            if (contexto.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
            {
                // Nunca se devuelve el detalle de la excepción.
                contexto.ProblemDetails.Title = "Ocurrió un error inesperado. Intenta de nuevo en unos minutos.";
                contexto.ProblemDetails.Detail = null;
                contexto.ProblemDetails.Extensions.Remove("exception");
            }
            else if (TitulosEnEspanol.TryGetValue(contexto.ProblemDetails.Status ?? 0, out var titulo)
                && contexto.ProblemDetails.Title is null or "Bad Request" or "Not Found" or "Method Not Allowed" or "Unsupported Media Type")
            {
                contexto.ProblemDetails.Title = titulo;
            }
            else if (contexto.ProblemDetails.Status == StatusCodes.Status401Unauthorized
                && contexto.ProblemDetails.Title is null or "Unauthorized")
            {
                contexto.ProblemDetails.Title = "Necesitas ingresar para continuar.";
            }
            else if (contexto.ProblemDetails.Status == StatusCodes.Status403Forbidden
                && contexto.ProblemDetails.Title is null or "Forbidden")
            {
                contexto.ProblemDetails.Title = "No tienes permiso para hacer esto.";
            }
        });

    // Persistencia: la hora sale de TimeProvider (también la usa el interceptor de auditoría). La cadena
    // ConnectionStrings:Nitido se valida al arrancar; sin ella la API no inicia (ver el catch de abajo).
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AgregarPersistenciaNitido();

    // El IContextoSesion del host (usuario y firma de los reclamos; cliente activo ya validado por el filtro de ruta).
    builder.Services.AgregarApiNitido();
    builder.Services.AgregarSeguridadWeb();
    builder.Services
        .AgregarFirmas(builder.Configuration)
        .AgregarClientes(builder.Configuration);

    // /salud responde 200 solo si SQL Server contesta a tiempo; si no, 503.
    builder.Services.AddHealthChecks()
        .AddCheck<ComprobacionSqlServer>("sqlserver", timeout: TimeSpan.FromSeconds(3));
    builder.Services.AddOpenApi();

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // No se registran cuerpos de petición, cookies ni el encabezado Authorization:
    // solo método, ruta, código de estado y duración.
    // Se usa el registrador del host (con la configuración y los destinos de este host) y no el
    // estático, porque con preserveStaticLogger el estático sigue siendo el registrador inicial.
    app.UseSerilogRequestLogging(opciones =>
    {
        opciones.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
        opciones.GetLevel = (contexto, _, excepcion) =>
            excepcion is not null || contexto.Response.StatusCode >= StatusCodes.Status500InternalServerError
                ? LogEventLevel.Error
                : contexto.Request.Path.StartsWithSegments("/salud", StringComparison.OrdinalIgnoreCase)
                    ? LogEventLevel.Verbose
                    : LogEventLevel.Information;
    });

    // Orden: la protección contra falsificación primero (también cubre las peticiones sin sesión), luego la
    // autenticación, el límite de peticiones y la autorización.
    app.UsarProteccionContraFalsificacion();
    app.UsarSeguridadWeb();

    app.MapHealthChecks("/salud", new HealthCheckOptions { AllowCachingResponses = false })
        .AllowAnonymous();

    app.MapearFirmas();
    app.MapearClientes();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.Run();
    return 0;
}
catch (OptionsValidationException ex)
{
    // Error de configuración (por ejemplo, falta la cadena de conexión): se registra el mensaje, que dice
    // qué hacer, sin la pila ni ningún valor de configuración.
    Log.Fatal("{Mensaje:l}", ex.Message);
    return 1;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "La API no pudo arrancar");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

// Necesario para que WebApplicationFactory<Program> funcione en las pruebas de integración.
public partial class Program;
