using System.Net;
using System.Text.Json;
using Nitido.Firmas.Infraestructura.Autenticacion;

namespace Nitido.Pruebas.Integracion;

/// <summary>F0-03b: TOTP obligatorio, inscripción, verificación, códigos de recuperación y almacenamiento.</summary>
public class SegundoFactorPruebas(ContenedorSqlServer contenedor)
{
    private const string MensajeDeCodigo = "El código no es válido. Revisa que la hora de tu teléfono esté bien y vuelve a intentarlo.";

    [Fact]
    public async Task Ingreso_SinSegundoFactor_PideInscribirlo_YConSegundoFactor_PideElCodigo()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);

        using (var primero = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, primero.StatusCode);
            Assert.Equal("inscribir-segundo-factor", (await ClienteDeApi.JsonAsync(primero, token)).GetProperty("paso").GetString());
        }

        await e.InscribirSegundoFactorAsync(token);
        using (var salida = await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
            Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);
        }

        using var segundo = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token);
        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.Equal("segundo-factor", (await ClienteDeApi.JsonAsync(segundo, token)).GetProperty("paso").GetString());
    }

    [Fact]
    public async Task Inscripcion_EntregaLaClaveYLaUriOtpauth()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);
        using (var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        }

        using var respuesta = await e.Cliente.GetAsync("/api/autenticacion/segundo-factor/inscripcion", token);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await ClienteDeApi.JsonAsync(respuesta, token);
        var clave = cuerpo.GetProperty("claveManual").GetString()!;
        var uri = new Uri(cuerpo.GetProperty("uriOtpauth").GetString()!);
        Assert.Equal("otpauth", uri.Scheme);
        Assert.Equal("totp", uri.Host);
        Assert.Contains($"secret={clave}", uri.Query, StringComparison.Ordinal);
        Assert.Contains("digits=6", uri.Query, StringComparison.Ordinal);
        Assert.Contains("period=30", uri.Query, StringComparison.Ordinal);
        Assert.Equal(32, clave.Length); // 20 bytes en base32, sin relleno
    }

    [Fact]
    public async Task Inscripcion_CodigoInvalidoResponde400ConElMensaje()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);
        using (var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        }

        using var inscripcion = await e.Cliente.GetAsync("/api/autenticacion/segundo-factor/inscripcion", token);
        var clave = (await ClienteDeApi.JsonAsync(inscripcion, token)).GetProperty("claveManual").GetString()!;
        var invalido = e.CodigoTotp(clave) == "000000" ? "111111" : "000000";

        using var respuesta = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor/inscripcion", new { codigo = invalido }, token);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(MensajeDeCodigo, (await ClienteDeApi.JsonAsync(respuesta, token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Inscripcion_ConCodigoValidoEntregaDiezCodigosDeRecuperacionUnaSolaVez()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, _, codigos) = await e.RegistrarEInscribirAsync(token);

        Assert.Equal(10, codigos.Length);
        Assert.Equal(10, codigos.Distinct().Count());
        Assert.All(codigos, c => Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", c));

        // Una vez inscrito, no se puede volver a inscribir ni a pedir otra clave.
        using var otraVez = await e.Cliente.GetAsync("/api/autenticacion/segundo-factor/inscripcion", token);
        Assert.NotEqual(HttpStatusCode.OK, otraVez.StatusCode);
    }

    [Fact]
    public async Task SegundoFactor_CodigoValidoDaSesionCompleta_E_InvalidoResponde401()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, clave, _) = await e.RegistrarEInscribirAsync(token);
        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        using (var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        }

        // Inválido: 401 con el mismo mensaje en title, y sigue sin haber sesión completa.
        using (var malo = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = "123456" == e.CodigoTotp(clave, 1) ? "654321" : "123456" }, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, malo.StatusCode);
            Assert.Equal("application/problem+json", malo.Content.Headers.ContentType?.MediaType);
            Assert.Equal(MensajeDeCodigo, (await ClienteDeApi.JsonAsync(malo, token)).GetProperty("title").GetString());
        }

        using (var sinSesion = await e.Cliente.GetAsync("/api/autenticacion/yo", token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);
        }

        // Válido: el código del paso siguiente (el de la inscripción ya se usó), 200 y sesión completa.
        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddSeconds(30);
        using var bueno = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = e.CodigoTotp(clave) }, token);
        Assert.Equal(HttpStatusCode.OK, bueno.StatusCode);
        using var conSesion = await e.Cliente.GetAsync("/api/autenticacion/yo", token);
        Assert.Equal(HttpStatusCode.OK, conSesion.StatusCode);
    }

    [Fact]
    public async Task Totp_CodigoUsadoNoSirveOtraVez()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, clave, _) = await e.RegistrarEInscribirAsync(token);

        // El código con el que se inscribió sigue dentro de su ventana, pero ya se usó.
        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        using (await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
        }

        using var repetido = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = e.CodigoTotp(clave) }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);

        // Y uno nuevo, de un paso posterior, sí sirve; usado una vez, ya no sirve una segunda.
        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddSeconds(60);
        var nuevo = e.CodigoTotp(clave);
        using var primero = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = nuevo }, token);
        Assert.Equal(HttpStatusCode.OK, primero.StatusCode);

        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        using (await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
        }

        using var segundo = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = nuevo }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, segundo.StatusCode);
    }

    [Fact]
    public async Task Totp_ToleranciaDeReloj()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, clave, _) = await e.RegistrarEInscribirAsync(token);

        // Pasan 90 segundos: el paso de la inscripción ya quedó atrás y hay pasos nuevos que probar.
        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddSeconds(90);

        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        // Hace 5 minutos: se rechaza.
        using (await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
        }

        using (var viejo = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = e.CodigoTotp(clave, -10) }, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, viejo.StatusCode);
        }

        // Hace 30 segundos (un paso atrás): se acepta.
        using (var reciente = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = e.CodigoTotp(clave, -1) }, token))
        {
            Assert.Equal(HttpStatusCode.OK, reciente.StatusCode);
        }
    }

    [Fact]
    public async Task SegundoFactor_FallosCuentanParaBloqueo()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, clave, _) = await e.RegistrarEInscribirAsync(token);
        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        using (await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
        }

        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddSeconds(60);
        var correcto = e.CodigoTotp(clave);
        var errado = correcto == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            using var intento = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = errado }, token);
            Assert.Equal(HttpStatusCode.Unauthorized, intento.StatusCode);
        }

        // Bloqueada: ni el código correcto sirve, ni tampoco la clave en un ingreso nuevo.
        using (var bloqueado = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = correcto }, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, bloqueado.StatusCode);
        }

        using (var ingresoBloqueado = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, ingresoBloqueado.StatusCode);
        }

        // Pasados los 15 minutos todo vuelve a funcionar.
        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddMinutes(16);
        using (var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        }

        using var bueno = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = e.CodigoTotp(clave) }, token);
        Assert.Equal(HttpStatusCode.OK, bueno.StatusCode);
    }

    [Fact]
    public async Task SegundoFactor_VolverAIngresarConLaClaveNoReiniciaElBloqueo()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, clave, _) = await e.RegistrarEInscribirAsync(token);
        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddSeconds(60);
        var correcto = e.CodigoTotp(clave);
        var errado = correcto == "000000" ? "111111" : "000000";

        // Cuatro códigos errados, volver a ingresar con la clave (que es lo que haría quien ya la tiene) y un quinto:
        // el contador no se reinició, así que la cuenta queda bloqueada y ni el código correcto sirve.
        using (await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
        }

        for (var i = 0; i < 4; i++)
        {
            using var intento = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = errado }, token);
            Assert.Equal(HttpStatusCode.Unauthorized, intento.StatusCode);
        }

        using (var segundoIngreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, segundoIngreso.StatusCode);
        }

        using (var quinto = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = errado }, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, quinto.StatusCode);
        }

        using (var conCodigoCorrecto = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor", new { codigo = correcto }, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, conCodigoCorrecto.StatusCode);
        }

        using var ingresoBloqueado = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token);
        Assert.Equal(HttpStatusCode.Unauthorized, ingresoBloqueado.StatusCode);
    }

    [Fact]
    public async Task Recuperacion_CodigoValidoDaSesionYNoSirveDosVeces()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, _, codigos) = await e.RegistrarEInscribirAsync(token);

        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        using (await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
        }

        using (var uso = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor/recuperacion", new { codigo = codigos[0] }, token))
        {
            Assert.Equal(HttpStatusCode.OK, uso.StatusCode);
            Assert.Equal(9, (await ClienteDeApi.JsonAsync(uso, token)).GetProperty("codigosRestantes").GetInt32());
        }

        using (var conSesion = await e.Cliente.GetAsync("/api/autenticacion/yo", token))
        {
            Assert.Equal(HttpStatusCode.OK, conSesion.StatusCode);
        }

        // El mismo código, otra vez (con el espacio y las minúsculas que alguien podría escribir): 401.
        using (await e.Cliente.PostAsync("/api/autenticacion/salida", null, token))
        {
        }

        using (await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
        }

        using (var repetido = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor/recuperacion", new { codigo = codigos[0].ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) }, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
        }

        // Otro código de la lista sí sirve.
        using var otro = await e.Cliente.PostAsync("/api/autenticacion/segundo-factor/recuperacion", new { codigo = codigos[1] }, token);
        Assert.Equal(HttpStatusCode.OK, otro.StatusCode);
        Assert.Equal(8, (await ClienteDeApi.JsonAsync(otro, token)).GetProperty("codigosRestantes").GetInt32());
    }

    [Fact]
    public async Task SecretoTotp_GuardadoCifrado()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, clave, _) = await e.RegistrarEInscribirAsync(token);

        var usuario = await e.LeerUsuarioAsync(correo, token);

        Assert.False(string.IsNullOrWhiteSpace(usuario.SecretoTotp));
        Assert.DoesNotContain(clave, usuario.SecretoTotp, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(clave, usuario.SecretoTotp);
        Assert.True(usuario.SegundoFactorActivo);
    }

    [Fact]
    public async Task CodigosRecuperacion_GuardadosConHash()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, _, codigos) = await e.RegistrarEInscribirAsync(token);

        var usuario = await e.LeerUsuarioAsync(correo, token);

        Assert.False(string.IsNullOrWhiteSpace(usuario.CodigosRecuperacionHash));
        var guardados = JsonSerializer.Deserialize<string[]>(usuario.CodigosRecuperacionHash!)!;
        Assert.Equal(10, guardados.Length);
        foreach (var codigo in codigos)
        {
            Assert.DoesNotContain(codigo, usuario.CodigosRecuperacionHash, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(codigo.Replace("-", string.Empty, StringComparison.Ordinal), usuario.CodigosRecuperacionHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Vectores de la RFC 6238 (apéndice B): secreto «12345678901234567890», SHA-1, los últimos 6 de 8 dígitos.</summary>
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Totp_CoincideConLosVectoresDeLaRfc6238(long segundos, string esperado)
    {
        var secreto = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal(esperado, Totp.Codigo(secreto, segundos / 30));
    }

    [Fact]
    public void Base32_IdaYVuelta_ConLaRfc4648()
    {
        // Vector de la RFC 4648: «foobar» = MZXW6YTBOI (sin relleno).
        Assert.Equal("MZXW6YTBOI", Base32.Codificar("foobar"u8));
        Assert.Equal("foobar"u8.ToArray(), Base32.Decodificar("MZXW6YTBOI======"));

        var secreto = Totp.GenerarSecreto();
        Assert.Equal(secreto, Base32.Decodificar(Base32.Codificar(secreto)));
        Assert.Throws<FormatException>(() => Base32.Decodificar("0189"));
    }
}
