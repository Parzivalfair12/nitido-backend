using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nitido.Compartido.Infraestructura.Persistencia;

namespace Nitido.Compartido.Infraestructura;

/// <summary>Registro de la persistencia común: cadena de conexión validada e interceptores.</summary>
public static class PersistenciaNitido
{
    /// <summary>Mensaje cuando falta la cadena de conexión. Se muestra tal cual al desarrollador.</summary>
    public const string MensajeSinCadenaDeConexion =
        "Falta la cadena de conexión ConnectionStrings:Nitido. Configúrala con dotnet user-secrets (ver README).";

    /// <summary>Clave de la configuración donde vive la cadena de conexión: <c>ConnectionStrings:Nitido</c>.</summary>
    public const string NombreDeLaCadena = "Nitido";

    /// <summary>
    /// Registra <see cref="OpcionesBaseDeDatos"/> (leídas de <c>ConnectionStrings:Nitido</c>, con validación al
    /// arrancar), el interceptor de auditoría y el que fija el <c>SESSION_CONTEXT</c> de SQL Server. Se llama una vez, desde el host, antes de los módulos.
    /// El host debe registrar también <see cref="TimeProvider"/>.
    /// </summary>
    public static IServiceCollection AgregarPersistenciaNitido(this IServiceCollection servicios)
    {
        servicios.AddOptions<OpcionesBaseDeDatos>()
            .Configure<IConfiguration>((opciones, configuracion) =>
                opciones.CadenaDeConexion = configuracion.GetConnectionString(NombreDeLaCadena) ?? string.Empty)
            .Validate(o => !string.IsNullOrWhiteSpace(o.CadenaDeConexion), MensajeSinCadenaDeConexion)
            .ValidateOnStart();

        servicios.TryAddSingleton<InterceptorDeAuditoria>();
        servicios.TryAddSingleton<InterceptorDeContextoSql>();
        return servicios;
    }

    /// <summary>
    /// Configura un <c>DbContext</c> de módulo: SQL Server con la cadena <c>ConnectionStrings:Nitido</c>, el
    /// historial de migraciones propio del módulo (<c>__EFMigrationsHistory_{modulo}</c>), reintentos ante fallas
    /// transitorias y los interceptores de auditoría y de contexto de sesión.
    /// </summary>
    public static DbContextOptionsBuilder UsarSqlServerNitido(
        this DbContextOptionsBuilder opciones,
        IServiceProvider servicios,
        string modulo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulo);

        // Si falta la cadena, leer Value lanza OptionsValidationException con el mensaje del criterio.
        var cadena = servicios.GetRequiredService<IOptions<OpcionesBaseDeDatos>>().Value.CadenaDeConexion;
        return opciones
            .UseSqlServer(
                cadena,
                sql =>
                {
                    sql.MigrationsHistoryTable($"__EFMigrationsHistory_{modulo}");
                    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                })
            .AddInterceptors(
                servicios.GetRequiredService<InterceptorDeAuditoria>(),
                servicios.GetRequiredService<InterceptorDeContextoSql>());
    }
}
