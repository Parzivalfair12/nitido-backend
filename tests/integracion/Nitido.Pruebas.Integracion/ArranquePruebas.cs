using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nitido.Compartido.Infraestructura;

namespace Nitido.Pruebas.Integracion;

public class ArranquePruebas
{
    private const string MensajeDelCriterio =
        "Falta la cadena de conexión ConnectionStrings:Nitido. Configúrala con dotnet user-secrets (ver README).";

    /// <summary>
    /// La API no arranca sin cadena y registra el texto exacto del criterio. Se ejecuta el punto de entrada
    /// real (<c>Program.cs</c>) en el mismo proceso y se lee la consola: con <c>WebApplicationFactory</c> no se
    /// puede ver la <see cref="OptionsValidationException"/>, porque el <c>catch</c> de Program.cs la captura
    /// (el factory termina con «ObjectDisposedException: IServiceProvider» y la oculta). Los argumentos de
    /// línea de comandos van de último y ganan a user-secrets, variables de entorno y archivos.
    /// Una cadena vacía o solo con espacios cuenta como faltante.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Arranque_SinCadenaDeConexionNoArranca(string cadenaFaltante)
    {
        var puntoDeEntrada = typeof(Program).Assembly.EntryPoint
            ?? throw new InvalidOperationException("Nitido.Api no tiene punto de entrada.");
        var salidaOriginal = Console.Out;
        var captura = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(captura));
        int codigoDeSalida;
        try
        {
            string[] argumentos = [$"--ConnectionStrings:Nitido={cadenaFaltante}", "--urls=http://127.0.0.1:0"];
            var ejecucion = Task.Run(() => puntoDeEntrada.Invoke(null, [argumentos]));
            var terminoAntesDelLimite = await Task.WhenAny(ejecucion, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)) == ejecucion;
            Assert.True(terminoAntesDelLimite, "La API siguió corriendo sin cadena de conexión: debía fallar al arrancar.");
            codigoDeSalida = (int)(await ejecucion)!;
        }
        finally
        {
            Console.SetOut(salidaOriginal);
        }

        var salida = captura.ToString();
        Assert.Equal(1, codigoDeSalida);

        // Un solo registro Fatal (JSON compacto fuera de Development, texto «FTL» en Development) con el texto
        // exacto del criterio, sin la pila ni el tipo de la excepción: es un error de configuración.
        // El propio host además registra «Hosting failed to start» con la pila en nivel Error; eso no cuenta.
        var fatales = salida
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Contains("\"@l\":\"Fatal\"", StringComparison.Ordinal) || l.Contains(" FTL]", StringComparison.Ordinal))
            .ToList();
        var fatal = Assert.Single(fatales);
        Assert.Contains(MensajeDelCriterio, fatal, StringComparison.Ordinal);
        Assert.DoesNotContain("\"@x\"", fatal, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", fatal, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", salida, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Arranque_LaValidacionRechazaLaCadenaFaltanteConElMensajeExacto(string? cadena)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(cadena is null ? [] : [new KeyValuePair<string, string?>("ConnectionStrings:Nitido", cadena)])
            .Build();
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IConfiguration>(configuracion);
        servicios.AddSingleton(TimeProvider.System);
        servicios.AgregarPersistenciaNitido();
        using var proveedor = servicios.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => proveedor.GetRequiredService<IOptions<OpcionesBaseDeDatos>>().Value);

        Assert.Equal(MensajeDelCriterio, ex.Message);
    }

    [Fact]
    public void Arranque_LaValidacionAceptaUnaCadenaConValor()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("ConnectionStrings:Nitido", "Server=x;Database=y")])
            .Build();
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IConfiguration>(configuracion);
        servicios.AddSingleton(TimeProvider.System);
        servicios.AgregarPersistenciaNitido();
        using var proveedor = servicios.BuildServiceProvider();

        Assert.Equal("Server=x;Database=y", proveedor.GetRequiredService<IOptions<OpcionesBaseDeDatos>>().Value.CadenaDeConexion);
    }

    [Fact]
    public void Arranque_ElMensajeDeLaConstanteEsElDelCriterio()
    {
        Assert.Equal(MensajeDelCriterio, PersistenciaNitido.MensajeSinCadenaDeConexion);
    }

    [Fact]
    public async Task Arranque_LaCadenaDeLaFabricaGanaALasVariablesDeEntorno()
    {
        // Prueba que FabricaNitido de verdad pisa a otras fuentes (user-secrets y variables de entorno),
        // para que las pruebas den el mismo resultado en cualquier equipo.
        const string Variable = "ConnectionStrings__Nitido";
        var anterior = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, "Server=del-entorno;Database=x");
        try
        {
            await using var fabrica = new FabricaNitido("Server=de-la-fabrica;Database=y");

            var opciones = fabrica.Services.GetRequiredService<IOptions<OpcionesBaseDeDatos>>().Value;

            Assert.Equal("Server=de-la-fabrica;Database=y", opciones.CadenaDeConexion);
            Assert.Equal("Server=de-la-fabrica;Database=y", fabrica.Services.GetRequiredService<IConfiguration>().GetConnectionString("Nitido"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, anterior);
        }
    }
}
