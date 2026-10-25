using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Nitido.Pruebas.Integracion;

public class OpenApiPruebas(ContenedorSqlServer contenedor)
{
    [Fact]
    public async Task OpenApi_SoloEnDevelopment()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);

        await using (var fabricaDevelopment = new FabricaNitido(cadena)
            .WithWebHostBuilder(b => b.UseEnvironment("Development")))
        {
            using var cliente = fabricaDevelopment.CreateClient();
            using var respuesta = await cliente.GetAsync("/openapi/v1.json", token);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        }

        await using (var fabricaProduction = new FabricaNitido(cadena)
            .WithWebHostBuilder(b => b.UseEnvironment("Production")))
        {
            using var cliente = fabricaProduction.CreateClient();
            using var respuesta = await cliente.GetAsync("/openapi/v1.json", token);

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }
    }
}
