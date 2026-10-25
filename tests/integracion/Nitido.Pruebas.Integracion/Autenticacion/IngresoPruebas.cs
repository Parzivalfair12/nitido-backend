using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Firmas.Infraestructura.Persistencia;
using Serilog.Core;
using Serilog.Events;

namespace Nitido.Pruebas.Integracion;

/// <summary>F0-03a: contraseña, ingreso, bloqueo, límite de peticiones, sesión y protecciones.</summary>
public class IngresoPruebas(ContenedorSqlServer contenedor)
{
    private const string TituloDeCredenciales = "El correo o la contraseña no coinciden.";

    [Fact]
    public async Task Clave_SeGuardaConHash()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);

        var usuario = await e.LeerUsuarioAsync(correo, token);

        Assert.False(string.IsNullOrWhiteSpace(usuario.ClaveHash));
        Assert.DoesNotContain(EscenarioDeAutenticacion.ClaveSegura, usuario.ClaveHash, StringComparison.Ordinal);
        var hasheador = new PasswordHasher<Usuario>();
        Assert.NotEqual(PasswordVerificationResult.Failed, hasheador.VerifyHashedPassword(usuario, usuario.ClaveHash, EscenarioDeAutenticacion.ClaveSegura));
        Assert.Equal(PasswordVerificationResult.Failed, hasheador.VerifyHashedPassword(usuario, usuario.ClaveHash, "otra-clave-larga-1"));
    }

    [Fact]
    public async Task Clave_LargoMinimoSaleDeConfiguracion()
    {
        var token = TestContext.Current.CancellationToken;

        // Con el valor por defecto (12), once caracteres no alcanzan y doce sí.
        await using (var porDefecto = await EscenarioDeAutenticacion.CrearAsync(contenedor, token))
        {
            await porDefecto.Cliente.PrepararAsync(token);
            using var corta = await RegistrarConClaveAsync(porDefecto, new string('a', 11), token);
            Assert.Equal(HttpStatusCode.BadRequest, corta.StatusCode);
            Assert.Equal(
                "La contraseña debe tener al menos 12 caracteres.",
                (await ClienteDeApi.JsonAsync(corta, token)).GetProperty("errors").GetProperty("administrador.clave")[0].GetString());

            using var justa = await RegistrarConClaveAsync(porDefecto, new string('a', 12), token);
            Assert.Equal(HttpStatusCode.Created, justa.StatusCode);
        }

        // Cambiando la configuración a 20, quince caracteres ya no alcanzan.
        await using var estricta = await EscenarioDeAutenticacion.CrearAsync(
            contenedor, token, new Dictionary<string, string?> { ["Seguridad:Clave:LargoMinimo"] = "20" });
        await estricta.Cliente.PrepararAsync(token);
        using var quince = await RegistrarConClaveAsync(estricta, new string('a', 15), token);
        Assert.Equal(HttpStatusCode.BadRequest, quince.StatusCode);
        Assert.Equal(
            "La contraseña debe tener al menos 20 caracteres.",
            (await ClienteDeApi.JsonAsync(quince, token)).GetProperty("errors").GetProperty("administrador.clave")[0].GetString());
    }

    [Fact]
    public async Task Ingreso_RespuestaIgualParaCorreoInexistenteYClaveErrada()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        const string ClaveErrada = "una-clave-equivocada-1";

        // Un usuario por caso: clave errada, usuario inactivo y cuenta bloqueada.
        var (_, conClaveErrada) = await e.RegistrarAsync(token);
        var (_, inactivo) = await e.RegistrarAsync(token);
        var usuarioInactivo = await e.LeerUsuarioAsync(inactivo, token);
        await e.EjecutarComoUsuarioAsync(usuarioInactivo, "UPDATE T_USUARIOS SET ACTIVO = 0 WHERE UUID_USUARIOS = @uuid", token);
        var (_, bloqueado) = await e.RegistrarAsync(token);
        for (var i = 0; i < 5; i++)
        {
            using var intento = await e.IngresarAsync(bloqueado, ClaveErrada, token);
            Assert.Equal(HttpStatusCode.Unauthorized, intento.StatusCode);
        }

        var casos = new (string Caso, string Correo, string Clave)[]
        {
            ("clave errada", conClaveErrada, ClaveErrada),
            ("correo inexistente", EscenarioDeAutenticacion.CorreoNuevo(), EscenarioDeAutenticacion.ClaveSegura),
            ("usuario inactivo", inactivo, EscenarioDeAutenticacion.ClaveSegura),
            ("cuenta bloqueada con la clave correcta", bloqueado, EscenarioDeAutenticacion.ClaveSegura),
        };

        var cuerpos = new List<SortedDictionary<string, string>>();
        foreach (var (caso, correo, clave) in casos)
        {
            using var respuesta = await e.IngresarAsync(correo, clave, token);

            Assert.True(respuesta.StatusCode == HttpStatusCode.Unauthorized, $"«{caso}» respondió {(int)respuesta.StatusCode} y debía responder 401.");
            Assert.False(e.Cliente.TieneCookie("nitido.parcial"), $"«{caso}» entregó una sesión.");
            var cuerpo = await CuerpoDelProblemaAsync(respuesta, token);
            Assert.Equal(TituloDeCredenciales, cuerpo["title"]);
            Assert.Equal("401", cuerpo["status"]);
            Assert.False(cuerpo.ContainsKey("mensaje"), "El cuerpo no debe traer un campo «mensaje»: el texto va en title.");
            cuerpos.Add(cuerpo);
        }

        Assert.All(cuerpos, c => Assert.Equal(cuerpos[0], c));
    }

    [Fact]
    public async Task Ingreso_BloqueaTrasIntentosFallidos()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);

        for (var i = 0; i < 5; i++)
        {
            using var intento = await e.IngresarAsync(correo, "una-clave-equivocada-1", token);
            Assert.Equal(HttpStatusCode.Unauthorized, intento.StatusCode);
        }

        // Bloqueada: ni la clave correcta sirve, ni siquiera casi al final de los 15 minutos.
        using (var bloqueada = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, bloqueada.StatusCode);
        }

        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddMinutes(14);
        using (var todaviaBloqueada = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, todaviaBloqueada.StatusCode);
        }

        // Pasados los 15 minutos, la clave correcta vuelve a servir.
        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddMinutes(2);
        using var desbloqueada = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token);
        Assert.Equal(HttpStatusCode.OK, desbloqueada.StatusCode);
    }

    [Fact]
    public async Task Ingreso_LaClaveCorrectaNoReiniciaElContadorDeIntentos()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);

        // Cuatro fallos y un acierto: el acierto no reinicia el contador (solo lo hace completar el segundo factor),
        // así que un quinto fallo ya bloquea la cuenta.
        for (var i = 0; i < 4; i++)
        {
            using var intento = await e.IngresarAsync(correo, "una-clave-equivocada-1", token);
            Assert.Equal(HttpStatusCode.Unauthorized, intento.StatusCode);
        }

        using (var acierto = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, acierto.StatusCode);
        }

        using (var quinto = await e.IngresarAsync(correo, "una-clave-equivocada-1", token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, quinto.StatusCode);
        }

        using var bloqueada = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token);
        Assert.Equal(HttpStatusCode.Unauthorized, bloqueada.StatusCode);
    }

    [Fact]
    public async Task Ingreso_LimiteDePeticionesPorMinuto_Responde429LaPeticionOnce()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor, token, new Dictionary<string, string?> { ["Seguridad:LimitePeticiones:PorMinuto"] = "10" });

        await e.Cliente.PrepararAsync(token);
        for (var i = 1; i <= 10; i++)
        {
            using var permitida = await e.Cliente.PostAsync("/api/autenticacion/ingreso", new { correo = "a@b.co", clave = "una-clave-larga-123" }, token);
            Assert.True(permitida.StatusCode == HttpStatusCode.Unauthorized, $"La petición {i} respondió {(int)permitida.StatusCode} y debía pasar el limitador (401 por credenciales).");
        }

        using var once = await e.Cliente.PostAsync("/api/autenticacion/ingreso", new { correo = "a@b.co", clave = "una-clave-larga-123" }, token);
        Assert.Equal(HttpStatusCode.TooManyRequests, once.StatusCode);
        Assert.Equal("application/problem+json", once.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Yo_NoCuentaParaElLimiteDePeticiones()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor, token, new Dictionary<string, string?> { ["Seguridad:LimitePeticiones:PorMinuto"] = "3" });

        // Recargar la aplicación llama a /yo muchas veces por minuto: nunca debe responder 429.
        for (var i = 0; i < 15; i++)
        {
            using var respuesta = await e.Cliente.GetAsync("/api/autenticacion/yo", token);
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }
    }

    [Fact]
    public async Task Sesion_VenceALasHorasMaximasAunqueSeUseSinParar()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor, token, new Dictionary<string, string?> { ["Seguridad:Sesion:HorasMaximas"] = "1" });
        await e.RegistrarEInscribirAsync(token);

        // Se usa cada 20 minutos (la inactividad de 30 minutos nunca se cumple), pero a la hora ya no sirve.
        foreach (var _ in new[] { 1, 2 })
        {
            e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddMinutes(20);
            using var activa = await e.Cliente.GetAsync("/api/autenticacion/yo", token);
            Assert.Equal(HttpStatusCode.OK, activa.StatusCode);
        }

        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddMinutes(21);
        using var vencida = await e.Cliente.GetAsync("/api/autenticacion/yo", token);
        Assert.Equal(HttpStatusCode.Unauthorized, vencida.StatusCode);
    }

    [Fact]
    public async Task Yo_ConSesion_EntregaNombreCorreoRolYFirma()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (correo, _, _) = await e.RegistrarEInscribirAsync(token);

        using var respuesta = await e.Cliente.GetAsync("/api/autenticacion/yo", token);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await ClienteDeApi.JsonAsync(respuesta, token);
        Assert.Equal("Ana Pérez", cuerpo.GetProperty("nombre").GetString());
        Assert.Equal(correo, cuerpo.GetProperty("correo").GetString());
        Assert.Equal("ADMINISTRADOR", cuerpo.GetProperty("rol").GetString());
        Assert.Equal("Firma de prueba", cuerpo.GetProperty("firma").GetString());
    }

    [Fact]
    public async Task Yo_SinSesion_Responde401ProblemDetailsYNuncaUnaRedireccion()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);

        using var respuesta = await e.Cliente.GetAsync("/api/autenticacion/yo", token);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Null(respuesta.Headers.Location);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Necesitas ingresar para continuar.", (await ClienteDeApi.JsonAsync(respuesta, token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Salida_Responde204YLaSiguienteLlamadaAYoResponde401()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await e.RegistrarEInscribirAsync(token);
        using (var conSesion = await e.Cliente.GetAsync("/api/autenticacion/yo", token))
        {
            Assert.Equal(HttpStatusCode.OK, conSesion.StatusCode);
        }

        using var salida = await e.Cliente.PostAsync("/api/autenticacion/salida", null, token);
        Assert.Equal(HttpStatusCode.NoContent, salida.StatusCode);

        using var despues = await e.Cliente.GetAsync("/api/autenticacion/yo", token);
        Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
    }

    [Fact]
    public async Task Sesion_VencePorInactividad()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await e.RegistrarEInscribirAsync(token);

        // La cookie de sesión vive 30 minutos; sin pedir nada en 31, ya no sirve.
        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddMinutes(31);

        using var respuesta = await e.Cliente.GetAsync("/api/autenticacion/yo", token);
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Sesion_LaConfiguracionCambiaLosMinutosDeInactividad()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor, token, new Dictionary<string, string?> { ["Seguridad:Sesion:MinutosInactividad"] = "5" });
        await e.RegistrarEInscribirAsync(token);

        e.Fabrica.Reloj.Ahora = e.Fabrica.Reloj.Ahora.AddMinutes(6);

        using var respuesta = await e.Cliente.GetAsync("/api/autenticacion/yo", token);
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Ingreso_LasCookiesSonHttpOnlySecureYSameSiteStrict()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);

        using (var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        }

        AssertAtributosDeSeguridad(e.Cliente.SetCookie("nitido.parcial"));

        await e.InscribirSegundoFactorAsync(token);
        AssertAtributosDeSeguridad(e.Cliente.SetCookie("nitido.sesion"));
    }

    [Fact]
    public async Task Ingreso_ConLaMismaSesionLaCookieDeSesionLlevaElUsuarioYLaFirma()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (uuidFirma, correo) = await e.RegistrarAsync(token);
        using (var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        }

        var usuario = await e.LeerUsuarioAsync(correo, token);
        Assert.Equal(uuidFirma, usuario.UuidFirmas);
        Assert.True(e.Cliente.TieneCookie("nitido.parcial"));
        Assert.False(e.Cliente.TieneCookie("nitido.sesion"), "Con solo la clave no debe haber sesión completa.");
    }

    [Fact]
    public async Task Antifalsificacion_UnPostSinEncabezadoResponde400()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        await e.Cliente.PrepararAsync(token);

        // La cookie XSRF-TOKEN llegó y es legible por el navegador (sin HttpOnly), pero el POST no trae el encabezado.
        var cookie = e.Cliente.SetCookie("XSRF-TOKEN");
        Assert.NotNull(cookie);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);

        using var sinEncabezado = await e.Cliente.PostAsync(
            "/api/autenticacion/ingreso",
            new { correo = "a@b.co", clave = "una-clave-larga-123" },
            token,
            conAntifalsificacion: false);
        Assert.Equal(HttpStatusCode.BadRequest, sinEncabezado.StatusCode);
        Assert.Equal("application/problem+json", sinEncabezado.Content.Headers.ContentType?.MediaType);

        // Con el encabezado, la misma petición llega al ingreso (y falla por credenciales, no por el token).
        using var conEncabezado = await e.Cliente.PostAsync(
            "/api/autenticacion/ingreso", new { correo = "a@b.co", clave = "una-clave-larga-123" }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, conEncabezado.StatusCode);
    }

    [Fact]
    public async Task SesionParcial_NoDaAccesoAlResto()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);
        using (var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token))
        {
            Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        }

        // Con la cookie parcial, todo lo que no sea inscripción o verificación responde 401.
        foreach (var ruta in new[] { "/api/autenticacion/yo", "/api/clientes", $"/api/clientes/{Guid.NewGuid()}" })
        {
            using var respuesta = await e.Cliente.GetAsync(ruta, token);
            Assert.True(respuesta.StatusCode == HttpStatusCode.Unauthorized, $"GET {ruta} respondió {(int)respuesta.StatusCode} y debía responder 401.");
        }

        foreach (var ruta in new[] { "/api/autenticacion/salida", "/api/clientes" })
        {
            using var respuesta = await e.Cliente.PostAsync(ruta, new { razonSocial = "X", numeroDocumento = "800197268" }, token);
            Assert.True(respuesta.StatusCode == HttpStatusCode.Unauthorized, $"POST {ruta} respondió {(int)respuesta.StatusCode} y debía responder 401.");
        }

        // Control: lo que sí es de la sesión parcial funciona.
        using var inscripcion = await e.Cliente.GetAsync("/api/autenticacion/segundo-factor/inscripcion", token);
        Assert.Equal(HttpStatusCode.OK, inscripcion.StatusCode);
    }

    [Fact]
    public async Task Endpoints_ExigenSesionSalvoListaPermitida()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        using var cliente = new ClienteDeApi(e.Fabrica); // arranca el host
        await cliente.PrepararAsync(token);

        // Salud, ingreso y registro son los únicos que se pueden pedir sin sesión; el contrato OpenAPI solo existe en
        // Development y no lleva datos.
        string[] anonimos = ["/salud", "/api/autenticacion/ingreso", "/api/firmas/registro", "/openapi/{documentName}.json"];

        var endpoints = e.Fabrica.Services.GetServices<EndpointDataSource>().SelectMany(f => f.Endpoints).OfType<RouteEndpoint>().ToList();
        Assert.True(endpoints.Count >= 10, $"Se esperaban al menos 10 endpoints registrados y hay {endpoints.Count}: ¿se leyó bien el origen de datos?");

        var problemas = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var ruta = endpoint.RoutePattern.RawText ?? "?";
            var permiteAnonimo = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var exigeSesion = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;
            if (anonimos.Contains(ruta, StringComparer.Ordinal))
            {
                continue;
            }

            if (permiteAnonimo || !exigeSesion)
            {
                problemas.Add($"{endpoint.DisplayName} ({ruta}) no exige sesión.");
            }
        }

        Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));

        // Control: la lista permitida existe de verdad entre los endpoints (si no, la prueba no vigilaría nada).
        Assert.Contains(endpoints, ep => ep.RoutePattern.RawText == "/api/autenticacion/ingreso" && ep.Metadata.GetMetadata<IAllowAnonymous>() is not null);
        Assert.Contains(endpoints, ep => ep.RoutePattern.RawText == "/api/firmas/registro" && ep.Metadata.GetMetadata<IAllowAnonymous>() is not null);
    }

    [Fact]
    public async Task Registro_DeshabilitadoResponde404()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor, token, new Dictionary<string, string?> { ["Registro:Habilitado"] = "false" });
        await e.Cliente.PrepararAsync(token);

        using var respuesta = await e.Cliente.PostAsync(
            "/api/firmas/registro",
            new { nombreFirma = "Firma", administrador = new { nombre = "Ana", correo = EscenarioDeAutenticacion.CorreoNuevo(), clave = EscenarioDeAutenticacion.ClaveSegura } },
            token);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Registro_CreaLaFirmaYElAdministradorQueIngresaEInscribeElSegundoFactor()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (uuidFirma, correo) = await e.RegistrarAsync(token);

        var usuario = await e.LeerUsuarioAsync(correo, token);
        Assert.Equal(uuidFirma, usuario.UuidFirmas);
        Assert.Equal("ADMINISTRADOR", usuario.Rol);
        Assert.True(usuario.Activo);

        using var ingreso = await e.IngresarAsync(correo, EscenarioDeAutenticacion.ClaveSegura, token);
        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        Assert.Equal("inscribir-segundo-factor", (await ClienteDeApi.JsonAsync(ingreso, token)).GetProperty("paso").GetString());
    }

    [Fact]
    public async Task Registro_CorreoRepetidoResponde409()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await EscenarioDeAutenticacion.CrearAsync(contenedor, token);
        var (_, correo) = await e.RegistrarAsync(token);

        using var repetido = await RegistrarConClaveAsync(e, EscenarioDeAutenticacion.ClaveSegura, token, correo.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("Ya existe una cuenta con ese correo.", (await ClienteDeApi.JsonAsync(repetido, token)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Ingreso_NoEscribeLaClaveEnRegistros()
    {
        var token = TestContext.Current.CancellationToken;
        var sumidero = new SumideroEnMemoria();
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor, token, serviciosAdicionales: servicios => servicios.AddSingleton<ILogEventSink>(sumidero));
        const string ClaveSecreta = "clave-que-no-debe-aparecer-9";
        var (_, correo) = await e.RegistrarAsync(token, clave: ClaveSecreta);

        using (var correcta = await e.IngresarAsync(correo, ClaveSecreta, token))
        {
            Assert.Equal(HttpStatusCode.OK, correcta.StatusCode);
        }

        using (var errada = await e.IngresarAsync(correo, "otra-clave-que-tampoco-9", token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
        }

        // Control: el sumidero sí recibe los registros de las peticiones (si no, la prueba pasaría en vacío).
        Assert.Contains(sumidero.Eventos, ev => ev.RenderMessage().Contains("/api/autenticacion/ingreso", StringComparison.Ordinal));
        foreach (var evento in sumidero.Eventos)
        {
            var texto = evento.RenderMessage() + " " + string.Join(" ", evento.Properties.Values.Select(v => v.ToString())) + " " + evento.Exception;
            Assert.DoesNotContain(ClaveSecreta, texto, StringComparison.Ordinal);
            Assert.DoesNotContain("otra-clave-que-tampoco-9", texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SemillaDesarrollo_CreaElUsuarioInicialEnDevelopment()
    {
        var token = TestContext.Current.CancellationToken;
        var correo = EscenarioDeAutenticacion.CorreoNuevo();
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor,
            token,
            new Dictionary<string, string?> { ["Desarrollo:UsuarioInicial:Correo"] = correo, ["Desarrollo:UsuarioInicial:Clave"] = "clave-de-desarrollo-1" },
            entorno: "Development");
        using var cliente = new ClienteDeApi(e.Fabrica); // arranca el host y con él la siembra
        await cliente.PrepararAsync(token);

        var usuario = await e.LeerUsuarioAsync(correo, token);
        Assert.Equal("ADMINISTRADOR", usuario.Rol);

        using var ingreso = await e.IngresarAsync(correo, "clave-de-desarrollo-1", token);
        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
    }

    [Fact]
    public async Task SemillaDesarrollo_NoCorreFueraDeDevelopment()
    {
        var token = TestContext.Current.CancellationToken;
        var correo = EscenarioDeAutenticacion.CorreoNuevo();
        await using var e = await EscenarioDeAutenticacion.CrearAsync(
            contenedor,
            token,
            new Dictionary<string, string?> { ["Desarrollo:UsuarioInicial:Correo"] = correo, ["Desarrollo:UsuarioInicial:Clave"] = "clave-de-desarrollo-1" },
            entorno: "Production");
        using var cliente = new ClienteDeApi(e.Fabrica);
        using var salud = await cliente.GetAsync("/salud", token); // arranca el host
        Assert.Equal(HttpStatusCode.OK, salud.StatusCode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.LeerUsuarioAsync(correo, token));
    }

    private static Task<HttpResponseMessage> RegistrarConClaveAsync(EscenarioDeAutenticacion e, string clave, CancellationToken token, string? correo = null) =>
        e.Cliente.PostAsync(
            "/api/firmas/registro",
            new { nombreFirma = "Firma de prueba", administrador = new { nombre = "Ana Pérez", correo = correo ?? EscenarioDeAutenticacion.CorreoNuevo(), clave } },
            token);

    private static void AssertAtributosDeSeguridad(string? setCookie)
    {
        Assert.NotNull(setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
    }

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

    private sealed class SumideroEnMemoria : ILogEventSink
    {
        private readonly List<LogEvent> _eventos = [];

        public IReadOnlyList<LogEvent> Eventos
        {
            get
            {
                lock (_eventos)
                {
                    return [.. _eventos];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_eventos)
            {
                _eventos.Add(logEvent);
            }
        }
    }
}
