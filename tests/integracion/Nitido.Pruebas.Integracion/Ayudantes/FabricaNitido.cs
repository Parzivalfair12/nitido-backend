using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// El host de la API para las pruebas. Siempre pone la cadena de conexión que se le da con
/// <c>ConfigureAppConfiguration</c>, que va de último y gana a los user-secrets y a las variables de
/// entorno del equipo (así las pruebas pasan igual en local y en CI). Registra el reloj y, por defecto, el
/// contexto de sesión de prueba solo aquí: el host de producción no los tiene.
/// Ninguna prueba debe usar <c>new WebApplicationFactory&lt;Program&gt;()</c> directamente.
/// </summary>
/// <param name="cadenaDeConexion">Valor de <c>ConnectionStrings:Nitido</c>. Puede ser vacío para probar su falta.</param>
/// <param name="usarSesionDePrueba">
/// Con <c>true</c> (por defecto) reemplaza <see cref="IContextoSesion"/> por <see cref="Sesion"/> (singleton; las
/// pruebas corren en serie). Con <c>false</c> no toca el registro del host: queda el de producción
/// (<c>ContextoSesionHttp</c>). Ya no existe la opción de quitar <see cref="IContextoSesion"/>, porque el host
/// siempre lo registra (F0-02b).
/// </param>
/// <param name="configuracion">Valores de configuración que cambian los de las pruebas (por ejemplo <c>Registro:Habilitado</c>).</param>
/// <param name="entorno">Entorno del host. Si es <c>null</c>, el de <c>WebApplicationFactory</c> (Development).</param>
/// <param name="serviciosAdicionales">Servicios que la prueba agrega al host (por ejemplo, un sumidero de Serilog).</param>
public sealed class FabricaNitido(
    string cadenaDeConexion,
    bool usarSesionDePrueba = true,
    IReadOnlyDictionary<string, string?>? configuracion = null,
    string? entorno = null,
    Action<IServiceCollection>? serviciosAdicionales = null) : WebApplicationFactory<Program>
{
    // Llavero de Data Protection de las pruebas: una carpeta temporal, para no escribir en la carpeta de la API.
    private static readonly string CarpetaDelLlavero = Path.Combine(Path.GetTempPath(), "nitido-pruebas-llavero");

    /// <summary>Instante fijo por defecto, distinto de la hora real y con milisegundos exactos.</summary>
    public static readonly DateTimeOffset InstanteInicial = new(2031, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);

    public RelojDePrueba Reloj { get; } = new(InstanteInicial);

    /// <summary>
    /// Quién actúa en las operaciones de esta fábrica. Arranca sin sesión (todo <see cref="Guid.Empty"/>): cada
    /// prueba llama a <see cref="ContextoSesionDePrueba.Actuar(Guid, Guid, Guid?)"/>. Solo tiene efecto si
    /// <c>usarSesionDePrueba</c> es <c>true</c>.
    /// </summary>
    public ContextoSesionDePrueba Sesion { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (entorno is not null)
        {
            builder.UseEnvironment(entorno);
        }

        builder.ConfigureAppConfiguration((_, valores) =>
        {
            valores.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("ConnectionStrings:Nitido", cadenaDeConexion),
                new KeyValuePair<string, string?>("Seguridad:Llavero:Directorio", CarpetaDelLlavero),

                // Las pruebas hacen muchas peticiones a /api/autenticacion/* desde la misma IP: el límite real se prueba aparte.
                new KeyValuePair<string, string?>("Seguridad:LimitePeticiones:PorMinuto", "100000"),
                new KeyValuePair<string, string?>("Desarrollo:UsuarioInicial:Correo", string.Empty),
                new KeyValuePair<string, string?>("Desarrollo:UsuarioInicial:Clave", string.Empty),
            ]);
            if (configuracion is not null)
            {
                valores.AddInMemoryCollection(configuracion);
            }
        });

        builder.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<TimeProvider>();
            servicios.AddSingleton<TimeProvider>(Reloj);
            serviciosAdicionales?.Invoke(servicios);

            if (usarSesionDePrueba)
            {
                servicios.RemoveAll<IContextoSesion>();
                servicios.AddSingleton<IContextoSesion>(Sesion);
            }
        });
    }
}
