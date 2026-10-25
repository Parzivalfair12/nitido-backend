using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Api.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// El filtro del cliente activo (F0-02b, 6.1): valida que el <c>{uuidCliente}</c> de las rutas
/// <c>/api/clientes/{uuidCliente}/...</c> esté entre los clientes a los que el usuario de la sesión puede entrar, y
/// responde <c>404</c> (nunca <c>403</c>) igual en los tres casos de «no», para no revelar que el cliente existe.
/// Usa los endpoints de <see cref="EndpointsDePrueba"/>, que solo existen en las pruebas y se arman con
/// <c>MapearRutasDelCliente</c>: el filtro real, el <c>IAccesoAClientes</c> real, la base real y las dos políticas.
/// </summary>
public class ClienteActivoPruebas(ContenedorSqlServer contenedor)
{
    private const string TituloDelNoEncontrado = "No encontramos ese cliente.";

    /// <summary>
    /// El host con los endpoints de prueba. Usa el <c>IContextoSesion</c> de producción (<c>ContextoSesionHttp</c>):
    /// quién actúa entra por los reclamos de la petición, que arma <see cref="EndpointsDePrueba"/> a partir de
    /// <see cref="Quien"/>, como lo hará la cookie de F0-03a.
    /// </summary>
    private sealed class Anfitrion(FabricaNitido baseDeLaFabrica, WebApplicationFactory<Program> http, ContextoSesionDePrueba quienActua) : IAsyncDisposable
    {
        public HttpClient Cliente { get; } = http.CreateClient();

        public EndpointDataSource Endpoints => http.Services.GetRequiredService<EndpointDataSource>();

        public ContextoSesionDePrueba Quien => quienActua;

        public async ValueTask DisposeAsync()
        {
            Cliente.Dispose();
            await http.DisposeAsync();
            await baseDeLaFabrica.DisposeAsync();
        }
    }

    private static Anfitrion ArmarAnfitrion(string cadena, bool conRutaSinFiltro = false)
    {
        var quien = new ContextoSesionDePrueba();
        var baseDeLaFabrica = new FabricaNitido(cadena, usarSesionDePrueba: false);
        var http = baseDeLaFabrica.WithWebHostBuilder(constructor =>
            constructor.ConfigureTestServices(servicios =>
                servicios.AddSingleton<IStartupFilter>(new EndpointsDePrueba(quien, conRutaSinFiltro))));
        return new Anfitrion(baseDeLaFabrica, http, quien);
    }

    private static string Ruta(Guid cliente, string endpoint = EndpointsDePrueba.RutaDelEco) => $"/api/clientes/{cliente}/{endpoint}";

    /// <summary>El cuerpo del ProblemDetails sin lo que cambia en cada petición (<c>traceId</c>).</summary>
    private static async Task<SortedDictionary<string, string>> CuerpoDelProblemaAsync(HttpResponseMessage respuesta, CancellationToken token)
    {
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(token));
        var cuerpo = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var propiedad in documento.RootElement.EnumerateObject().Where(p => p.Name != "traceId"))
        {
            cuerpo[propiedad.Name] = propiedad.Value.ToString();
        }

        return cuerpo;
    }

    [Fact]
    public async Task ClienteEnRuta_SinAccesoResponde404()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var siembra = new FabricaNitido(cadena);
        var s = await SiembraDeDosFirmas.CrearAsync(siembra, cadena, token);
        await using var anfitrion = ArmarAnfitrion(cadena);

        // Como A1: CB1 (otra firma), CA2 (de su firma, sin acceso) y un cliente que no existe.
        anfitrion.Quien.Actuar(s.A1, s.FirmaA);
        var cuerpos = new List<(string Caso, SortedDictionary<string, string> Cuerpo)>();
        foreach (var (caso, cliente) in new[] { ("otra firma", s.CB1), ("sin acceso", s.CA2), ("no existe", Guid.CreateVersion7()) })
        {
            using var respuesta = await anfitrion.Cliente.GetAsync(Ruta(cliente), token);

            Assert.True(respuesta.StatusCode == HttpStatusCode.NotFound, $"«{caso}» respondió {(int)respuesta.StatusCode} y debía responder 404 (nunca 403).");
            var cuerpo = await CuerpoDelProblemaAsync(respuesta, token);
            Assert.Equal(TituloDelNoEncontrado, cuerpo["title"]);
            Assert.Equal("cliente_no_encontrado", cuerpo["codigo"]);
            Assert.Equal("404", cuerpo["status"]);
            Assert.False(cuerpo.ContainsKey("detail"), $"«{caso}» trae un detalle que podría delatar al cliente.");
            cuerpos.Add((caso, cuerpo));
        }

        // El mismo cuerpo en los tres casos: así no se revela que el cliente existe.
        Assert.All(cuerpos, c => Assert.Equal(cuerpos[0].Cuerpo, c.Cuerpo));

        // Sin sesión (sin reclamos), incluso CA1 responde 404, con el mismo cuerpo.
        anfitrion.Quien.Actuar(SesionSql.SinSesion);
        using (var respuesta = await anfitrion.Cliente.GetAsync(Ruta(s.CA1), token))
        {
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.Equal(cuerpos[0].Cuerpo, await CuerpoDelProblemaAsync(respuesta, token));
        }

        // Control positivo: con sesión, CA1 responde 200 y el eco devuelve CA1.
        anfitrion.Quien.Actuar(s.A1, s.FirmaA);
        using (var respuesta = await anfitrion.Cliente.GetAsync(Ruta(s.CA1), token))
        {
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(token));
            Assert.Equal(s.CA1, documento.RootElement.GetProperty("uuidEmpresas").GetGuid());
        }
    }

    [Fact]
    public async Task ClienteEnRuta_ValorQueNoEsGuidResponde404()
    {
        // La restricción :guid de la ruta responde 404 a un valor que no es GUID antes de llegar al filtro, así que
        // el cuerpo es el 404 genérico del host y no el «No encontramos ese cliente.» de los otros tres casos. La
        // especificación (6.2) lo da por el mismo cuerpo: ver el reporte del tester; aquí solo se exige 404 con
        // ProblemDetails y que no se llegue a ningún dato.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var siembra = new FabricaNitido(cadena);
        var s = await SiembraDeDosFirmas.CrearAsync(siembra, cadena, token);
        await using var anfitrion = ArmarAnfitrion(cadena);
        anfitrion.Quien.Actuar(s.A1, s.FirmaA);

        foreach (var valor in new[] { "no-es-un-guid", "123", "00000000-0000-0000-0000-00000000000z" })
        {
            using var respuesta = await anfitrion.Cliente.GetAsync($"/api/clientes/{valor}/{EndpointsDePrueba.RutaDelEco}", token);

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        }

        // Control: con un GUID válido de su cliente, la misma ruta sí responde.
        using var conGuid = await anfitrion.Cliente.GetAsync(Ruta(s.CA1), token);
        Assert.Equal(HttpStatusCode.OK, conGuid.StatusCode);
    }

    [Fact]
    public async Task ClienteEnRuta_CadaPeticionUsaSuPropioClienteActivo()
    {
        // El cliente activo viaja en la ruta, no en la sesión: dos pestañas con clientes distintos del mismo
        // usuario no se pisan. El cliente validado llega a IContextoSesion.UuidEmpresas y, desde ahí, a la
        // conexión que EF abre después (SESSION_CONTEXT), sin que una petición vea el cliente de otra.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var siembra = new FabricaNitido(cadena);
        var s = await SiembraDeDosFirmas.CrearAsync(siembra, cadena, token);
        var segundoCliente = await DarUnSegundoClienteAsync(siembra, s, token);
        await using var anfitrion = ArmarAnfitrion(cadena);
        anfitrion.Quien.Actuar(s.A1, s.FirmaA);

        // Peticiones simultáneas a dos clientes del mismo usuario.
        var peticiones = Enumerable.Range(0, 8)
            .Select(i => i % 2 == 0 ? s.CA1 : segundoCliente)
            .Select(async cliente =>
            {
                using var respuesta = await anfitrion.Cliente.GetAsync(Ruta(cliente), token);
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
                using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(token));
                return (Esperado: cliente, Eco: documento.RootElement.GetProperty("uuidEmpresas").GetGuid());
            })
            .ToList();
        foreach (var (esperado, eco) in await Task.WhenAll(peticiones))
        {
            Assert.Equal(esperado, eco);
        }

        // Lo que ve SQL Server en la conexión que abre EF después del filtro: el usuario, la firma y el cliente.
        foreach (var cliente in new[] { s.CA1, segundoCliente })
        {
            var sql = await anfitrion.Cliente.GetFromJsonAsync<JsonElement>(Ruta(cliente, EndpointsDePrueba.RutaDeSql), token);
            Assert.Equal(s.A1, sql.GetProperty("uuidUsuarios").GetGuid());
            Assert.Equal(s.FirmaA, sql.GetProperty("uuidFirmas").GetGuid());
            Assert.Equal(cliente, sql.GetProperty("uuidEmpresas").GetGuid());
        }

        // El cliente activo no queda en el host entre peticiones: otro usuario que pide el cliente de la
        // petición anterior no lo hereda ni lo ve.
        anfitrion.Quien.Actuar(s.A2, s.FirmaA);
        using var delOtroUsuario = await anfitrion.Cliente.GetAsync(Ruta(s.CA2), token);
        Assert.Equal(HttpStatusCode.OK, delOtroUsuario.StatusCode);
        using var siguiente = await anfitrion.Cliente.GetAsync(Ruta(s.CA1), token);
        Assert.Equal(HttpStatusCode.NotFound, siguiente.StatusCode);
    }

    /// <summary>Un segundo cliente de A1 (la siembra solo le da CA1): lo crea A1 con su acceso.</summary>
    private static async Task<Guid> DarUnSegundoClienteAsync(FabricaNitido fabrica, SiembraDeDosFirmas s, CancellationToken token)
    {
        var cliente = DatosDePrueba.NuevoCliente(fabrica, s.FirmaA, s.A1);
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);
        fabrica.Sesion.Actuar(SesionSql.SinSesion);
        return cliente.Uuid;
    }

    [Fact]
    public async Task RutasDelCliente_TodasValidanElClienteActivo()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        await using var anfitrion = ArmarAnfitrion(cadena);

        var rutasDeCliente = RutasConUuidCliente(anfitrion.Endpoints);
        var sinFiltro = rutasDeCliente.Where(r => !r.TieneElFiltro).Select(r => r.Patron).ToList();

        // Con los endpoints de prueba, para no pasar en vacío mientras el host no tenga rutas de cliente.
        Assert.True(rutasDeCliente.Count >= 2, $"No se encontraron las rutas de prueba de cliente. Rutas: [{string.Join(", ", rutasDeCliente.Select(r => r.Patron))}].");
        Assert.True(sinFiltro.Count == 0, "Rutas con {uuidCliente} sin ClienteActivoRequerido: " + string.Join(", ", sinFiltro));
    }

    [Fact]
    public async Task RutasDelCliente_LaPruebaDetectaUnaRutaArmadaAMano()
    {
        // Control negativo: una ruta con {uuidCliente} armada sin MapearRutasDelCliente se marca.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        await using var anfitrion = ArmarAnfitrion(cadena, conRutaSinFiltro: true);

        var rutasDeCliente = RutasConUuidCliente(anfitrion.Endpoints);
        var sinFiltro = rutasDeCliente.Where(r => !r.TieneElFiltro).Select(r => r.Patron).ToList();

        var ruta = Assert.Single(sinFiltro);
        Assert.Contains(EndpointsDePrueba.RutaSinFiltro, ruta, StringComparison.Ordinal);
        Assert.True(rutasDeCliente.Count(r => r.TieneElFiltro) >= 2, "Las rutas armadas con MapearRutasDelCliente deben seguir marcadas.");
    }

    private static List<(string Patron, bool TieneElFiltro)> RutasConUuidCliente(EndpointDataSource fuente) =>
    [
        .. fuente.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is not null && e.RoutePattern.RawText.Contains("{uuidCliente", StringComparison.OrdinalIgnoreCase))
            .Select(e => (e.RoutePattern.RawText!, e.Metadata.GetMetadata<ClienteActivoRequerido>() is not null)),
    ];
}
