using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Infraestructura;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Firmas.Infraestructura.Autenticacion;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Un host de la API con la cadena de conexión de una base migrada, un cliente HTTP con cookies y los pasos que
/// repiten todas las pruebas de ingreso: registrar una firma, ingresar, inscribir el segundo factor y calcular el
/// código TOTP de la hora del reloj de la fábrica.
/// </summary>
internal sealed class EscenarioDeAutenticacion : IAsyncDisposable
{
    public const string ClaveSegura = "una-clave-larga-123";

    private EscenarioDeAutenticacion(FabricaNitido fabrica, string cadena)
    {
        Fabrica = fabrica;
        Cadena = cadena;
        Cliente = new ClienteDeApi(fabrica);
    }

    public FabricaNitido Fabrica { get; }

    public string Cadena { get; }

    public ClienteDeApi Cliente { get; }

    public static async Task<EscenarioDeAutenticacion> CrearAsync(
        ContenedorSqlServer contenedor,
        CancellationToken token,
        IReadOnlyDictionary<string, string?>? configuracion = null,
        string? entorno = null,
        Action<IServiceCollection>? serviciosAdicionales = null)
    {
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        return new EscenarioDeAutenticacion(
            new FabricaNitido(cadena, usarSesionDePrueba: false, configuracion, entorno, serviciosAdicionales),
            cadena);
    }

    public static string CorreoNuevo() => $"usuario-{Guid.NewGuid():N}@ejemplo.co";

    public async Task<(Guid UuidFirma, string Correo)> RegistrarAsync(CancellationToken token, string? correo = null, string clave = ClaveSegura)
    {
        correo ??= CorreoNuevo();
        await Cliente.PrepararAsync(token);
        using var respuesta = await Cliente.PostAsync(
            "/api/firmas/registro",
            new { nombreFirma = "Firma de prueba", administrador = new { nombre = "Ana Pérez", correo, clave } },
            token);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await ClienteDeApi.JsonAsync(respuesta, token);
        return (cuerpo.GetProperty("uuidFirma").GetGuid(), correo);
    }

    public async Task<HttpResponseMessage> IngresarAsync(string correo, string clave, CancellationToken token)
    {
        await Cliente.PrepararAsync(token);
        return await Cliente.PostAsync("/api/autenticacion/ingreso", new { correo, clave }, token);
    }

    /// <summary>Inscribe el segundo factor de quien tiene la sesión parcial. Queda con sesión completa.</summary>
    public async Task<(string ClaveManual, string[] Codigos)> InscribirSegundoFactorAsync(CancellationToken token)
    {
        using var inscripcion = await Cliente.GetAsync("/api/autenticacion/segundo-factor/inscripcion", token);
        Assert.Equal(HttpStatusCode.OK, inscripcion.StatusCode);
        var clave = (await ClienteDeApi.JsonAsync(inscripcion, token)).GetProperty("claveManual").GetString()!;

        using var confirmacion = await Cliente.PostAsync(
            "/api/autenticacion/segundo-factor/inscripcion",
            new { codigo = CodigoTotp(clave) },
            token);
        Assert.Equal(HttpStatusCode.OK, confirmacion.StatusCode);
        var codigos = (await ClienteDeApi.JsonAsync(confirmacion, token))
            .GetProperty("codigosRecuperacion")
            .EnumerateArray()
            .Select(c => c.GetString()!)
            .ToArray();
        return (clave, codigos);
    }

    /// <summary>Registra una firma, ingresa e inscribe el segundo factor. Termina con sesión completa.</summary>
    public async Task<(string Correo, string ClaveManual, string[] Codigos)> RegistrarEInscribirAsync(CancellationToken token)
    {
        var (_, correo) = await RegistrarAsync(token);
        using var ingreso = await IngresarAsync(correo, ClaveSegura, token);
        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        var (clave, codigos) = await InscribirSegundoFactorAsync(token);
        return (correo, clave, codigos);
    }

    /// <summary>El código TOTP de la hora del reloj de la fábrica, desplazado por pasos de 30 segundos.</summary>
    public string CodigoTotp(string claveManual, int pasos = 0) =>
        Totp.Codigo(Base32.Decodificar(claveManual), Totp.PasoDe(Fabrica.Reloj.GetUtcNow()) + pasos);

    /// <summary>Lee al usuario de la base, sin filtros de EF, con la llave de ingreso por correo.</summary>
    internal async Task<Usuario> LeerUsuarioAsync(string correo, CancellationToken token)
    {
        using var alcance = Fabrica.Services.GetRequiredService<IServiceScopeFactory>().CrearAlcanceParaIngreso(correo.ToUpperInvariant());
        var baseDeDatos = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        return await baseDeDatos.Usuarios
            .IgnoreQueryFilters([DbContextNitido.FiltroMultiempresa])
            .AsNoTracking()
            .SingleAsync(u => u.CorreoNormalizado == correo.ToUpperInvariant(), token);
    }

    /// <summary>Cambia una columna del usuario con SQL directo, con su propia sesión (usuario y firma).</summary>
    public async Task EjecutarComoUsuarioAsync(Usuario usuario, string sql, CancellationToken token)
    {
        var filas = await ConsultasSql.EjecutarAsync(Cadena, new SesionSql(usuario.Uuid, usuario.UuidFirmas), sql, token, ("@uuid", usuario.Uuid));
        Assert.Equal(1, filas);
    }

    public async ValueTask DisposeAsync()
    {
        Cliente.Dispose();
        await Fabrica.DisposeAsync();
    }
}
