using System.Diagnostics;
using System.Net;

namespace Nitido.Pruebas.Integracion;

public class SaludPruebas(ContenedorSqlServer contenedor)
{
    // Puerto cerrado en la propia máquina: la conexión se rechaza sin necesidad de ningún contenedor.
    private const string CadenaSinBaseDeDatos =
        "Server=tcp:127.0.0.1,1;Database=nitido;User Id=sa;Password=x;Encrypt=False;Connect Timeout=2";

    [Fact]
    public async Task Salud_RespondeOk()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        using var cliente = fabrica.CreateClient();

        using var respuesta = await cliente.GetAsync("/salud", token);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(token);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", cuerpo);
        Assert.Equal("text/plain", respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Salud_Responde503SinBaseDeDatos()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fabrica = new FabricaNitido(CadenaSinBaseDeDatos);
        using var cliente = fabrica.CreateClient();

        var reloj = Stopwatch.StartNew();
        using var respuesta = await cliente.GetAsync("/salud", token);
        reloj.Stop();
        var cuerpo = await respuesta.Content.ReadAsStringAsync(token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("Unhealthy", cuerpo);
        Assert.True(
            reloj.Elapsed < TimeSpan.FromSeconds(3),
            $"/salud tardó {reloj.Elapsed.TotalSeconds:F2} s en responder 503; debe tardar menos de 3 s.");
    }
}
