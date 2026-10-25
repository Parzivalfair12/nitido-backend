using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Registro de la API (Serilog). El destino en memoria se inyecta como servicio
/// <see cref="ILogEventSink"/>; la configuración del host lo recoge con ReadFrom.Services.
/// </summary>
public class RegistroPruebas(ContenedorSqlServer contenedor)
{
    [Fact]
    public async Task Salud_NoGeneraRegistroDeNivelInformation()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        var destino = new DestinoEnMemoria();
        await using var fabrica = new FabricaNitido(cadena)
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<ILogEventSink>(destino)));
        using var cliente = fabrica.CreateClient();

        using (await cliente.GetAsync("/salud", token))
        {
        }

        // Control: una ruta cualquiera sí debe quedar en el registro de peticiones con Information.
        // Sin esto la prueba pasaría en vacío si el registro no estuviera conectado.
        using (await cliente.GetAsync("/no-existe", token))
        {
        }

        var informacion = destino.Eventos.Where(e => e.Level == LogEventLevel.Information).ToList();

        Assert.True(
            informacion.Any(e => e.RenderMessage().Contains("/no-existe", StringComparison.Ordinal)),
            "El registro de peticiones no dejó un registro Information para /no-existe: "
            + "Serilog no está conectado o UseSerilogRequestLogging falta.");
        Assert.DoesNotContain(
            informacion,
            e => e.RenderMessage().Contains("/salud", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Production_RegistraJsonCompactoConAplicacion()
    {
        var token = TestContext.Current.CancellationToken;
        var salida = await CapturarConsolaAsync("Production", await contenedor.CadenaDeBaseSinTablasAsync(token), token);

        var lineas = LineasNoVacias(salida);

        Assert.True(
            lineas.Any(l => l.Contains("/no-existe", StringComparison.Ordinal)),
            "No quedó ningún registro de la petición a /no-existe en la consola en Production.");

        // TODA línea de la consola debe ser JSON compacto de Serilog: un registro en texto plano
        // (por ejemplo un Fatal del registrador inicial o la continuación de una pila) rompe el
        // formato que leen las herramientas de observabilidad.
        foreach (var linea in lineas)
        {
            JsonDocument documento;
            try
            {
                documento = JsonDocument.Parse(linea);
            }
            catch (JsonException)
            {
                Assert.Fail($"En Production la línea de la consola no es JSON: {SinClaves(linea)}");
                return;
            }

            using var liberar = documento;
            var raiz = documento.RootElement;

            Assert.Equal(JsonValueKind.Object, raiz.ValueKind);
            Assert.True(raiz.TryGetProperty("@t", out _), $"No es JSON compacto de Serilog (falta @t): {SinClaves(linea)}");
            Assert.True(raiz.TryGetProperty("Aplicacion", out var aplicacion), $"Falta Aplicacion: {SinClaves(linea)}");
            Assert.Equal("nitido-api", aplicacion.GetString());
        }
    }

    [Fact]
    public async Task Development_RegistraEnFormatoLegible()
    {
        var token = TestContext.Current.CancellationToken;
        var salida = await CapturarConsolaAsync("Development", await contenedor.CadenaDeBaseSinTablasAsync(token), token);

        var lineas = LineasNoVacias(salida);
        var lineaPeticion = lineas.FirstOrDefault(l => l.Contains("/no-existe", StringComparison.Ordinal));

        Assert.True(
            lineaPeticion is not null,
            "No quedó ningún registro de la petición a /no-existe en la consola en Development.");
        Assert.False(
            lineaPeticion.StartsWith('{'),
            $"En Development la consola no debe emitir JSON: {SinClaves(lineaPeticion)}");
        Assert.True(
            Regex.IsMatch(lineaPeticion, @"^\[\d{2}:\d{2}:\d{2} INF\]"),
            $"En Development la línea no tiene el formato legible esperado: {SinClaves(lineaPeticion)}");

        // En Development ninguna línea debe ser JSON compacto de Serilog.
        Assert.DoesNotContain(lineas, l => l.StartsWith("{\"@t\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// Arranca el host en el entorno indicado, hace una petición a una ruta inexistente y devuelve
    /// todo lo escrito en la consola. La consola de Serilog toma Console.Out al construirse, así que
    /// se redirige antes de crear el host.
    /// </summary>
    private static async Task<string> CapturarConsolaAsync(string entorno, string cadena, CancellationToken token)
    {
        var salidaOriginal = Console.Out;
        var captura = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(captura));
        try
        {
            await using var fabrica = new FabricaNitido(cadena)
                .WithWebHostBuilder(b => b.UseEnvironment(entorno));
            using var cliente = fabrica.CreateClient();
            using (await cliente.GetAsync("/no-existe", token))
            {
            }
        }
        finally
        {
            Console.SetOut(salidaOriginal);
        }

        return captura.ToString();
    }

    /// <summary>Las líneas de la consola vienen de un host con la cadena de conexión real: nunca se imprimen con su clave.</summary>
    private static string SinClaves(string texto) =>
        Regex.Replace(texto, @"(?i)(password|pwd)\s*=\s*[^;""\s]*", "$1=***");

    private static List<string> LineasNoVacias(string texto) =>
        [.. texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private sealed class DestinoEnMemoria : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _eventos = new();

        public IReadOnlyCollection<LogEvent> Eventos => _eventos;

        public void Emit(LogEvent logEvent) => _eventos.Enqueue(logEvent);
    }
}
