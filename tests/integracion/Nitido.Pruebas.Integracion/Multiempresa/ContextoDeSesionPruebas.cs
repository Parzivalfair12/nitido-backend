using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Api.Multiempresa;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// El contexto de sesión solo para pruebas (<see cref="ContextoSesionDePrueba"/>) no puede llegar al host. Las
/// pruebas de arquitectura ya impiden que un proyecto de producción referencie uno de pruebas; estas cubren lo que
/// ellas no ven: el registro en la inyección de dependencias.
/// </summary>
public class ContextoDeSesionPruebas(ContenedorSqlServer contenedor)
{
    /// <summary>
    /// Arma el host y copia su colección de servicios (un <c>ConfigureTestServices</c> que no cambia nada). Devuelve
    /// la copia y la fábrica base, de la que se leen los servicios y a la que le toca liberar el host.
    /// </summary>
    private async Task<(List<ServiceDescriptor> Registros, FabricaNitido Fabrica)> ArmarHostAsync(bool usarSesionDePrueba)
    {
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(TestContext.Current.CancellationToken);
        List<ServiceDescriptor> registros = [];
        var fabrica = new FabricaNitido(cadena, usarSesionDePrueba);
        var conCopia = fabrica.WithWebHostBuilder(constructor =>
            constructor.ConfigureTestServices(servicios => registros = [.. servicios]));
        _ = conCopia.Services; // construye el host: es lo que ejecuta ConfigureTestServices
        await conCopia.DisposeAsync();
        return (registros, fabrica);
    }

    [Fact]
    public async Task ContextoDePrueba_NoSeRegistraEnLaApi()
    {
        // FabricaNitido(cadena sin tablas, usarSesionDePrueba: false) y un ConfigureTestServices que solo copia la
        // colección de servicios del host.
        var (registros, fabrica) = await ArmarHostAsync(usarSesionDePrueba: false);
        await using var anfitrion = fabrica;

        // 1. Hay un solo registro de IContextoSesion, con implementación ContextoSesionHttp del ensamblado
        //    Nitido.Compartido.Api.
        var delContexto = registros.Where(r => r.ServiceType == typeof(IContextoSesion)).ToList();
        var unico = Assert.Single(delContexto);
        Assert.Equal(typeof(ContextoSesionHttp), unico.ImplementationType);
        Assert.Equal("Nitido.Compartido.Api", unico.ImplementationType!.Assembly.GetName().Name);

        // 2. Ningún registro de IContextoSesion tiene implementación, instancia o fábrica declarada en un
        //    ensamblado Nitido.Pruebas.*.
        foreach (var registro in delContexto)
        {
            var ensamblados = new[]
            {
                registro.ImplementationType?.Assembly,
                registro.ImplementationInstance?.GetType().Assembly,
                registro.ImplementationFactory?.Method.DeclaringType?.Assembly,
            };
            Assert.All(
                ensamblados.Where(a => a is not null),
                a => Assert.False(
                    a!.GetName().Name!.StartsWith("Nitido.Pruebas", StringComparison.Ordinal),
                    $"IContextoSesion se registra con algo de pruebas ({a.GetName().Name})."));
        }

        // Ni siquiera como servicio con llave o como otra interfaz que lo implemente.
        Assert.DoesNotContain(
            registros,
            r => r.ImplementationType == typeof(ContextoSesionDePrueba) || r.ImplementationInstance is ContextoSesionDePrueba);

        // 3. En un alcance, el servicio no es ContextoSesionDePrueba y, sin HttpContext, da Guid.Empty y null.
        using var alcance = fabrica.Services.CreateScope();
        var sesion = alcance.ServiceProvider.GetRequiredService<IContextoSesion>();
        Assert.IsNotType<ContextoSesionDePrueba>(sesion);
        Assert.IsType<ContextoSesionHttp>(sesion);
        Assert.Equal(Guid.Empty, sesion.UuidUsuarios);
        Assert.Equal(Guid.Empty, sesion.UuidFirmas);
        Assert.Null(sesion.UuidEmpresas);
    }

    [Fact]
    public async Task ContextoDePrueba_SoloEstaCuandoLaFabricaLoPide()
    {
        // Control de la prueba anterior: con usarSesionDePrueba: true la fábrica sí reemplaza el registro del host,
        // de modo que «no se registra» no es un resultado que dé siempre.
        var (registros, fabrica) = await ArmarHostAsync(usarSesionDePrueba: true);
        await using var anfitrion = fabrica;

        var delContexto = Assert.Single(registros, r => r.ServiceType == typeof(IContextoSesion));
        Assert.Same(fabrica.Sesion, delContexto.ImplementationInstance);
        using var alcance = fabrica.Services.CreateScope();
        Assert.Same(fabrica.Sesion, alcance.ServiceProvider.GetRequiredService<IContextoSesion>());
    }

    [Fact]
    public async Task ContextoSesionHttp_SeRegistraPorAlcance()
    {
        // Un contexto singleton con datos de la petición mostraría el usuario de una petición a la siguiente.
        var (registros, fabrica) = await ArmarHostAsync(usarSesionDePrueba: false);
        await using var anfitrion = fabrica;

        var registro = Assert.Single(registros, r => r.ServiceType == typeof(IContextoSesion));
        Assert.Equal(ServiceLifetime.Scoped, registro.Lifetime);
        Assert.Contains(registros, r => r.ServiceType == typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor));
    }

    [Fact]
    public async Task Interceptores_ElDeContextoSqlEstaRegistradoComoSingleton()
    {
        // AgregarPersistenciaNitido registra también InterceptorDeContextoSql (singleton), junto al de auditoría.
        var (registros, fabrica) = await ArmarHostAsync(usarSesionDePrueba: true);
        await using var anfitrion = fabrica;

        var delContexto = Assert.Single(registros, r => r.ServiceType.Name == "InterceptorDeContextoSql");
        Assert.Equal(ServiceLifetime.Singleton, delContexto.Lifetime);
        var deAuditoria = Assert.Single(registros, r => r.ServiceType.Name == "InterceptorDeAuditoria");
        Assert.Equal(ServiceLifetime.Singleton, deAuditoria.Lifetime);
    }
}
