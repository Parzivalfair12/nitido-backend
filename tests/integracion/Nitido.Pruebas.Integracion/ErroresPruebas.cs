using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Nitido.Pruebas.Integracion;

public class ErroresPruebas(ContenedorSqlServer contenedor)
{
    [Fact]
    public async Task RutaInexistente_RespondeProblemDetails()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        using var cliente = fabrica.CreateClient();

        using var respuesta = await cliente.GetAsync("/no-existe", token);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(token);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);

        using var documento = JsonDocument.Parse(cuerpo);
        Assert.Equal(404, documento.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Error500_RespondeProblemDetailsSinDetalle(string entorno)
    {
        const string textoSecreto = "secreto-interno-123";
        const string tituloEsperado = "Ocurrió un error inesperado. Intenta de nuevo en unos minutos.";
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);

        await using var fabrica = new FabricaNitido(cadena).WithWebHostBuilder(constructor =>
        {
            constructor.UseEnvironment(entorno);
            constructor.ConfigureTestServices(servicios =>
                servicios.AddSingleton<IStartupFilter, FiltroQueLanzaExcepcion>());
        });
        using var cliente = fabrica.CreateClient();

        using var respuesta = await cliente.GetAsync(RutaQueFalla, token);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(token);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);

        using var documento = JsonDocument.Parse(cuerpo);
        Assert.Equal(500, documento.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(tituloEsperado, documento.RootElement.GetProperty("title").GetString());

        Assert.DoesNotContain(textoSecreto, cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(ExcepcionDePrueba), cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.False(documento.RootElement.TryGetProperty("detail", out var detalle) && detalle.ValueKind != JsonValueKind.Null);
    }

    private const string RutaQueFalla = "/prueba-error-500";

    private sealed class ExcepcionDePrueba(string mensaje) : Exception(mensaje);

    // Agrega, solo para esta prueba, un middleware al final de la canalización que lanza la excepción.
    // Va después de la canalización de la aplicación, es decir, dentro del manejador de excepciones.
    private sealed class FiltroQueLanzaExcepcion : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) =>
            aplicacion =>
            {
                siguiente(aplicacion);
                aplicacion.Use((contexto, continuar) =>
                    contexto.Request.Path == RutaQueFalla
                        ? throw new ExcepcionDePrueba("secreto-interno-123")
                        : continuar());
            };
    }
}
