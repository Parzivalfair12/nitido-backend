using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Nitido.Compartido.Infraestructura;

namespace Nitido.Api.Configuracion;

/// <summary>
/// Comprobación de salud de SQL Server: abre una conexión y ejecuta <c>SELECT 1</c>. No usa los
/// <c>DbContext</c>, porque su estrategia de reintentos tardaría decenas de segundos en declarar caída la
/// base. Responde en 3 segundos como máximo (declara la caída a los 2,5 s, antes de que el servicio de salud cancele a los 3 s). No devuelve el detalle del error ni registra la cadena de conexión.
/// </summary>
internal sealed class ComprobacionSqlServer(
    IOptions<OpcionesBaseDeDatos> opciones,
    ILogger<ComprobacionSqlServer> registro) : IHealthCheck
{
    private const int SegundosDeEspera = 3;

    private static readonly TimeSpan TopeInterno = TimeSpan.FromSeconds(2.5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var consulta = EjecutarAsync(cancellationToken);

            // SqlClient no siempre atiende la cancelación mientras abre la conexión o espera la respuesta
            // (con el servidor congelado puede tardar más de 8 s), así que el tope se hace cumplir aquí, sin
            // esperar a que la consulta termine. Es un poco menor que los 3 s del servicio de salud: si fuera
            // igual, el servicio cancelaría el token a la vez que vence el tope y el aviso nunca se registraría.
            var tope = Task.Delay(TopeInterno, cancellationToken);
            if (await Task.WhenAny(consulta, tope) != consulta)
            {
                // La consulta abandonada termina sola (cierra su conexión); se observa su error para que no quede sin atender.
                _ = consulta.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                cancellationToken.ThrowIfCancellationRequested();
                registro.LogWarning(
                    "SQL Server no respondió en {Segundos} s a la comprobación de salud.",
                    TopeInterno.TotalSeconds);
                return HealthCheckResult.Unhealthy("SQL Server no responde.");
            }

            await consulta;
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("SQL Server no responde.");
        }
        catch (Exception ex)
        {
            // Cualquier falla (red, credenciales, cadena mal formada) significa que no hay base.
            registro.LogWarning("SQL Server no respondió a la comprobación de salud ({TipoDeError}).", ex.GetType().Name);
            return HealthCheckResult.Unhealthy("SQL Server no responde.");
        }
    }

    private async Task EjecutarAsync(CancellationToken cancellationToken)
    {
        var cadena = new SqlConnectionStringBuilder(opciones.Value.CadenaDeConexion)
        {
            ConnectRetryCount = 0,
        };
        if (cadena.ConnectTimeout is 0 or > SegundosDeEspera)
        {
            cadena.ConnectTimeout = SegundosDeEspera;
        }

        await using var conexion = new SqlConnection(cadena.ConnectionString);
        await conexion.OpenAsync(cancellationToken);

        await using var comando = conexion.CreateCommand();
        comando.CommandTimeout = SegundosDeEspera;
        comando.CommandText = "SELECT 1";
        await comando.ExecuteScalarAsync(cancellationToken);
    }
}
