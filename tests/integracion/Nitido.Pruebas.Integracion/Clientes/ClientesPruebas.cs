using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Clientes.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>F0-04: crear, listar y consultar clientes por HTTP, con sesión real y las dos barreras de multiempresa.</summary>
public class ClientesPruebas(ContenedorSqlServer contenedor)
{
    private static readonly string[] MensajesDeSoloNit = ["Por ahora solo puedes agregar clientes con NIT. Escribe los 9 dígitos, sin el dígito de verificación."];

    [Fact]
    public async Task Clientes_CrearDevuelve201ConLocationElDigitoCalculadoElAccesoYLaAuditoria()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, _, _) = await e.RegistrarEInscribirAsync(token);

        using var respuesta = await e.Cliente.PostAsync("/api/clientes", new { razonSocial = "  Acme S.A.S.  ", numeroDocumento = "800197268" }, token);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await ClienteDeApi.JsonAsync(respuesta, token);
        var uuid = cuerpo.GetProperty("uuid").GetGuid();
        Assert.Equal($"/api/clientes/{uuid}", respuesta.Headers.Location?.OriginalString);
        Assert.Equal("31", cuerpo.GetProperty("tipoDocumento").GetString());
        Assert.Equal("800197268", cuerpo.GetProperty("numeroDocumento").GetString());
        Assert.Equal("4", cuerpo.GetProperty("digitoVerificacion").GetString());
        Assert.Equal("Acme S.A.S.", cuerpo.GetProperty("razonSocial").GetString());
        Assert.True(cuerpo.GetProperty("activo").GetBoolean());

        // Quien lo crea queda con acceso, y las columnas de auditoría traen a ese usuario.
        var usuario = await e.LeerUsuarioAsync(correo, token);
        var sesion = new SesionSql(usuario.Uuid, usuario.UuidFirmas);
        var accesos = await ConsultasSql.EscalarAsync(
            e.Cadena, sesion, "SELECT COUNT(*) FROM T_USUARIOS_EMPRESAS WHERE UUID_EMPRESAS = @cliente AND UUID_USUARIOS = @usuario", token,
            ("@cliente", uuid), ("@usuario", usuario.Uuid));
        Assert.Equal(1, Convert.ToInt32(accesos));
        var creadoPor = await ConsultasSql.EscalarAsync(
            e.Cadena, sesion, "SELECT UUID_USUARIOS_CREACION FROM T_EMPRESAS WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", uuid));
        Assert.Equal(usuario.Uuid, (Guid)creadoPor!);
    }

    [Fact]
    public async Task Clientes_AceptaLosTresFormatosDelNit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await e.RegistrarEInscribirAsync(token);

        foreach (var (texto, numero) in new[] { ("890903938", "890903938"), ("860.002.964", "860002964"), ("899999068-1", "899999068") })
        {
            using var respuesta = await e.Cliente.PostAsync("/api/clientes", new { razonSocial = $"Cliente {numero}", numeroDocumento = texto }, token);

            Assert.True(respuesta.StatusCode == HttpStatusCode.Created, $"«{texto}» respondió {(int)respuesta.StatusCode}.");
            var cuerpo = await ClienteDeApi.JsonAsync(respuesta, token);
            Assert.Equal(numero, cuerpo.GetProperty("numeroDocumento").GetString());
            Assert.Equal(Nit.Calcular(numero), cuerpo.GetProperty("digitoVerificacion").GetString());
        }
    }

    [Fact]
    public async Task Clientes_DigitoDeVerificacionQueNoCorrespondeResponde400()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await e.RegistrarEInscribirAsync(token);

        using var respuesta = await e.Cliente.PostAsync("/api/clientes", new { razonSocial = "Acme", numeroDocumento = "800197268-9" }, token);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "El dígito de verificación no corresponde a ese NIT.",
            (await ClienteDeApi.JsonAsync(respuesta, token)).GetProperty("errors").GetProperty("numeroDocumento")[0].GetString());
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("700123456")]
    [InlineData("1020304050")]
    public async Task Clientes_SoloSeAceptaNitDeNueveDigitos(string documento)
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await e.RegistrarEInscribirAsync(token);

        using var respuesta = await e.Cliente.PostAsync("/api/clientes", new { razonSocial = "Acme", numeroDocumento = documento }, token);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(
            MensajesDeSoloNit,
            (await ClienteDeApi.JsonAsync(respuesta, token)).GetProperty("errors").GetProperty("numeroDocumento").EnumerateArray().Select(m => m.GetString()).ToArray());
    }

    [Fact]
    public async Task Clientes_LasValidacionesRespondenConSuMensajePorCampo()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await e.RegistrarEInscribirAsync(token);

        async Task<JsonElement> ErroresAsync(object cuerpo)
        {
            using var respuesta = await e.Cliente.PostAsync("/api/clientes", cuerpo, token);
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            return (await ClienteDeApi.JsonAsync(respuesta, token)).GetProperty("errors");
        }

        Assert.Equal(
            "Escribe la razón social o el nombre del cliente.",
            (await ErroresAsync(new { razonSocial = "   ", numeroDocumento = "800197268" })).GetProperty("razonSocial")[0].GetString());
        Assert.Equal(
            "La razón social puede tener hasta 200 caracteres.",
            (await ErroresAsync(new { razonSocial = new string('a', 201), numeroDocumento = "800197268" })).GetProperty("razonSocial")[0].GetString());
        Assert.Equal(
            "Escribe el NIT o la cédula del cliente.",
            (await ErroresAsync(new { razonSocial = "Acme", numeroDocumento = "" })).GetProperty("numeroDocumento")[0].GetString());
    }

    [Fact]
    public async Task Clientes_NitRepetidoEnLaMismaFirmaResponde409_PeroEnDosFirmasEsPermitido()
    {
        var token = TestContext.Current.CancellationToken;
        await using var a = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await using var b = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await a.RegistrarEInscribirAsync(token);
        await b.RegistrarEInscribirAsync(token);

        using (var primero = await a.Cliente.PostAsync("/api/clientes", new { razonSocial = "Acme", numeroDocumento = "800197268" }, token))
        {
            Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        }

        using (var repetido = await a.Cliente.PostAsync("/api/clientes", new { razonSocial = "Acme otra vez", numeroDocumento = "800.197.268-4" }, token))
        {
            Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
            Assert.Equal("Este cliente ya está en tu firma.", (await ClienteDeApi.JsonAsync(repetido, token)).GetProperty("title").GetString());
        }

        // Clientes_MismoNitEnDosFirmasEsPermitido: la otra firma sí puede tenerlo.
        using var enOtraFirma = await b.Cliente.PostAsync("/api/clientes", new { razonSocial = "Acme", numeroDocumento = "800197268" }, token);
        Assert.Equal(HttpStatusCode.Created, enOtraFirma.StatusCode);
    }

    [Fact]
    public async Task Clientes_IgnoraFirmaEnviadaEnElCuerpo()
    {
        var token = TestContext.Current.CancellationToken;
        await using var a = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await using var b = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correoA, _, _) = await a.RegistrarEInscribirAsync(token);
        var (correoB, _, _) = await b.RegistrarEInscribirAsync(token);
        var firmaAjena = (await b.LeerUsuarioAsync(correoB, token)).UuidFirmas;

        using var respuesta = await a.Cliente.PostAsync(
            "/api/clientes",
            new { razonSocial = "Acme", numeroDocumento = "800197268", uuidFirmas = firmaAjena, uuidFirma = firmaAjena },
            token);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var uuid = (await ClienteDeApi.JsonAsync(respuesta, token)).GetProperty("uuid").GetGuid();
        var usuarioA = await a.LeerUsuarioAsync(correoA, token);
        var firmaGuardada = await ConsultasSql.EscalarAsync(
            a.Cadena, new SesionSql(usuarioA.Uuid, usuarioA.UuidFirmas), "SELECT UUID_FIRMAS FROM T_EMPRESAS WHERE UUID_EMPRESAS = @c", token, ("@c", uuid));
        Assert.Equal(usuarioA.UuidFirmas, (Guid)firmaGuardada!);
        Assert.NotEqual(firmaAjena, (Guid)firmaGuardada!);
    }

    [Fact]
    public async Task Clientes_ListaSoloLosDeMiFirma_OrdenadosPorRazonSocial()
    {
        var token = TestContext.Current.CancellationToken;
        await using var a = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await using var b = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await a.RegistrarEInscribirAsync(token);
        await b.RegistrarEInscribirAsync(token);

        foreach (var (razon, nit) in new[] { ("Zapatería Zeta", "890903938"), ("Almacén Alfa", "800197268"), ("Molinos Mu", "860002964") })
        {
            using var creado = await a.Cliente.PostAsync("/api/clientes", new { razonSocial = razon, numeroDocumento = nit }, token);
            Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        }

        using (var ajeno = await b.Cliente.PostAsync("/api/clientes", new { razonSocial = "Cliente de la otra firma", numeroDocumento = "899999068" }, token))
        {
            Assert.Equal(HttpStatusCode.Created, ajeno.StatusCode);
        }

        using var listaA = await a.Cliente.GetAsync("/api/clientes", token);
        Assert.Equal(HttpStatusCode.OK, listaA.StatusCode);
        var razonesA = (await ClienteDeApi.JsonAsync(listaA, token)).EnumerateArray().Select(c => c.GetProperty("razonSocial").GetString()!).ToArray();
        Assert.Equal(["Almacén Alfa", "Molinos Mu", "Zapatería Zeta"], razonesA);

        using var listaB = await b.Cliente.GetAsync("/api/clientes", token);
        Assert.Equal(["Cliente de la otra firma"], (await ClienteDeApi.JsonAsync(listaB, token)).EnumerateArray().Select(c => c.GetProperty("razonSocial").GetString()!).ToArray());
    }

    [Fact]
    public async Task Clientes_UsuarioDeOtraFirmaRecibe404_YElPropioRecibe200()
    {
        var token = TestContext.Current.CancellationToken;
        await using var a = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await using var b = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await a.RegistrarEInscribirAsync(token);
        await b.RegistrarEInscribirAsync(token);
        Guid uuid;
        JsonElement creado;
        using (var respuesta = await a.Cliente.PostAsync("/api/clientes", new { razonSocial = "Acme", numeroDocumento = "800197268" }, token))
        {
            creado = await ClienteDeApi.JsonAsync(respuesta, token);
            uuid = creado.GetProperty("uuid").GetGuid();
        }

        using (var propio = await a.Cliente.GetAsync($"/api/clientes/{uuid}", token))
        {
            Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
            Assert.Equal(creado.ToString(), (await ClienteDeApi.JsonAsync(propio, token)).ToString());
        }

        using var ajeno = await b.Cliente.GetAsync($"/api/clientes/{uuid}", token);
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode); // 404, no 403: no se revela que existe
        Assert.Equal("No encontramos ese cliente.", (await ClienteDeApi.JsonAsync(ajeno, token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Clientes_SinSesionResponde401()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);

        using var lista = await e.Cliente.GetAsync("/api/clientes", token);
        Assert.Equal(HttpStatusCode.Unauthorized, lista.StatusCode);

        await e.Cliente.PrepararAsync(token);
        using var crear = await e.Cliente.PostAsync("/api/clientes", new { razonSocial = "Acme", numeroDocumento = "800197268" }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, crear.StatusCode);
    }

    [Fact]
    public async Task Clientes_ListaDeDosMilClientesRespondeEnMenosDe500Milisegundos()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, _, _) = await e.RegistrarEInscribirAsync(token);
        var usuario = await e.LeerUsuarioAsync(correo, token);

        // Siembra: 2.000 clientes de esa firma, cada uno con el acceso del usuario, con la sesión de ese usuario.
        await using (var siembra = new FabricaNitido(e.Cadena))
        {
            siembra.Sesion.Actuar(usuario.Uuid, usuario.UuidFirmas);
            using var alcance = siembra.Services.CreateScope();
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var ahora = siembra.Reloj.GetUtcNow();
            for (var i = 0; i < 2000; i++)
            {
                var numero = (900_000_000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
                contexto.Clientes.Add(Cliente.Crear(usuario.UuidFirmas, "31", numero, Nit.Calcular(numero), $"Cliente {i:0000}", usuario.Uuid, ahora));
            }

            await contexto.SaveChangesAsync(token);
        }

        // Una primera petición calienta el host (compilación de la consulta, conexión del grupo); la segunda se mide.
        using (var calentamiento = await e.Cliente.GetAsync("/api/clientes", token))
        {
            Assert.Equal(HttpStatusCode.OK, calentamiento.StatusCode);
        }

        var reloj = Stopwatch.StartNew();
        using var respuesta = await e.Cliente.GetAsync("/api/clientes", token);
        var cuerpo = await ClienteDeApi.JsonAsync(respuesta, token);
        reloj.Stop();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(2000, cuerpo.GetArrayLength());
        Assert.True(reloj.ElapsedMilliseconds < 500, $"La lista de 2.000 clientes tardó {reloj.ElapsedMilliseconds} ms (límite 500 ms).");
    }
}
