using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nitido.Compartido.Infraestructura;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Compartido.Multiempresa;
using Nitido.Compartido.Resultados;
using Nitido.Firmas.Aplicacion.Autenticacion;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Firmas.Infraestructura.Autenticacion;

/// <summary>
/// Autenticación de nítido (F0-03a y F0-03b): contraseña con el <c>PasswordHasher</c> de Identity, bloqueo por
/// intentos fallidos, TOTP obligatorio y códigos de recuperación. La cookie la maneja el host.
/// </summary>
/// <remarks>
/// El ingreso busca al usuario por correo antes de saber su firma, así que corre en un alcance propio con la llave
/// <c>CORREO_INGRESO</c> (la política de <c>T_USUARIOS</c> deja ver solo esa fila, ADR 0003). Una vez encontrado, los
/// contadores se escriben en otro alcance con el usuario y la firma encontrados. Las operaciones del doble factor
/// actúan con la sesión parcial de la petición. No se usa <c>UserManager</c> de Identity: sus almacenes no se
/// ajustan a las tablas temporales ni a la seguridad por fila, y el hash es lo único que hace falta de Identity.
/// </remarks>
internal sealed class ServicioDeAutenticacion(
    IServiceScopeFactory alcances,
    FirmasDbContext baseDeDatos,
    IContextoSesion sesion,
    TimeProvider reloj,
    IOptions<OpcionesDeSeguridadDeFirmas> opciones,
    IPasswordHasher<Usuario> hasheador,
    IDataProtectionProvider proteccion) : IServicioDeAutenticacion
{
    internal const string Emisor = "nítido";
    internal const int CantidadDeCodigosDeRecuperacion = 10;
    private const string AlfabetoDeRecuperacion = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int LargoDeCodigoDeRecuperacion = 10;

    private static readonly Error CredencialesInvalidas =
        Error.NoAutorizado("credenciales_invalidas", "El correo o la contraseña no coinciden.");

    private static readonly Error CodigoNoValido =
        Error.NoAutorizado("codigo_invalido", MensajeDeCodigoNoValido);

    private const string MensajeDeCodigoNoValido =
        "El código no es válido. Revisa que la hora de tu teléfono esté bien y vuelve a intentarlo.";

    private static readonly Usuario UsuarioFicticio = new();

    // Hash que se verifica cuando el correo no existe, para que ese camino tarde lo mismo que uno con usuario real.
    private static readonly string HashFicticio = new PasswordHasher<Usuario>().HashPassword(UsuarioFicticio, Guid.NewGuid().ToString("N"));

    private IDataProtector Protector => proteccion.CreateProtector("nitido.firmas.secreto-totp");

    public async Task<Resultado<ResultadoDeIngreso>> IngresarAsync(string correo, string clave, CancellationToken cancelacion)
    {
        var normalizado = Normalizar(correo);
        Usuario? encontrado = null;
        if (normalizado.Length > 0)
        {
            using var alcanceDeBusqueda = alcances.CrearAlcanceParaIngreso(normalizado);
            var busqueda = alcanceDeBusqueda.ServiceProvider.GetRequiredService<FirmasDbContext>();
            encontrado = await busqueda.Usuarios
                .IgnoreQueryFilters([DbContextNitido.FiltroMultiempresa])
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.CorreoNormalizado == normalizado, cancelacion);
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        if (encontrado is null || !encontrado.Activo || encontrado.ClaveHash is null)
        {
            hasheador.VerifyHashedPassword(UsuarioFicticio, HashFicticio, clave ?? string.Empty);
            return CredencialesInvalidas;
        }

        var coincide = hasheador.VerifyHashedPassword(encontrado, encontrado.ClaveHash, clave ?? string.Empty)
            != PasswordVerificationResult.Failed;
        var bloqueada = encontrado.BloqueoHasta > ahora;

        // Los contadores se escriben con el usuario y la firma ya conocidos.
        using (var alcanceDeEscritura = alcances.CrearAlcanceConContexto(encontrado.Uuid, encontrado.UuidFirmas))
        {
            var escritura = alcanceDeEscritura.ServiceProvider.GetRequiredService<FirmasDbContext>();
            var usuario = await escritura.Usuarios.FirstOrDefaultAsync(u => u.Uuid == encontrado.Uuid, cancelacion);
            if (usuario is null)
            {
                return CredencialesInvalidas;
            }

            if (bloqueada)
            {
                return CredencialesInvalidas;
            }

            if (!coincide)
            {
                await RegistrarFalloAsync(escritura, usuario, ahora, cancelacion);
                return CredencialesInvalidas;
            }

            // La clave correcta NO reinicia el contador: los fallos del segundo factor suman en el mismo, y si lo reiniciara
            // quien ya tiene la clave podría probar códigos sin límite (ingresar, 4 códigos, ingresar...). El contador se
            // reinicia solo cuando se completa el segundo factor (código, recuperación o inscripción).
        }

        return new ResultadoDeIngreso(
            encontrado.SegundoFactorActivo ? PasoDeIngreso.SegundoFactor : PasoDeIngreso.InscribirSegundoFactor,
            new IdentidadDeSesion(encontrado.Uuid, encontrado.UuidFirmas));
    }

    public async Task<Resultado<InscripcionDeSegundoFactor>> ObtenerInscripcionAsync(CancellationToken cancelacion)
    {
        var usuario = await UsuarioDeLaSesionAsync(cancelacion);
        if (usuario is null)
        {
            return CodigoNoValido;
        }

        if (usuario.SegundoFactorActivo)
        {
            return Error.Conflicto("segundo_factor_ya_inscrito", "Tu doble factor ya está activo.");
        }

        var claveManual = Base32.Codificar(Totp.GenerarSecreto());
        usuario.SecretoTotp = Protector.Protect(claveManual);
        usuario.SelloSeguridad = Guid.NewGuid();
        await baseDeDatos.SaveChangesAsync(cancelacion);

        return new InscripcionDeSegundoFactor(claveManual, Totp.UriOtpauth(Emisor, usuario.Correo, claveManual));
    }

    public async Task<Resultado<CodigosDeRecuperacionEmitidos>> ConfirmarInscripcionAsync(string codigo, CancellationToken cancelacion)
    {
        var usuario = await UsuarioDeLaSesionAsync(cancelacion);
        if (usuario is null)
        {
            return CodigoNoValido;
        }

        if (usuario.SegundoFactorActivo)
        {
            return Error.Conflicto("segundo_factor_ya_inscrito", "Tu doble factor ya está activo.");
        }

        var ahora = reloj.GetUtcNow();
        var paso = usuario.SecretoTotp is null
            ? null
            : Totp.Verificar(Base32.Decodificar(Protector.Unprotect(usuario.SecretoTotp)), LimpiarCodigo(codigo), ahora, null);
        if (paso is null)
        {
            await RegistrarFalloAsync(baseDeDatos, usuario, ahora.UtcDateTime, cancelacion);
            return new Error(TipoError.Validacion, "codigo_invalido", MensajeDeCodigoNoValido);
        }

        var codigos = GenerarCodigosDeRecuperacion();
        usuario.SegundoFactorActivo = true;
        usuario.TotpUltimoPaso = paso;
        usuario.CodigosRecuperacionHash = JsonSerializer.Serialize(codigos.Select(Hash));
        usuario.IntentosFallidos = 0;
        usuario.BloqueoHasta = null;
        usuario.SelloSeguridad = Guid.NewGuid();
        await baseDeDatos.SaveChangesAsync(cancelacion);

        return new CodigosDeRecuperacionEmitidos(
            new IdentidadDeSesion(usuario.Uuid, usuario.UuidFirmas),
            codigos.Select(DarFormato).ToList());
    }

    public async Task<Resultado<IdentidadDeSesion>> VerificarSegundoFactorAsync(string codigo, CancellationToken cancelacion)
    {
        var usuario = await UsuarioDeLaSesionAsync(cancelacion);
        if (usuario is null || !usuario.SegundoFactorActivo || usuario.SecretoTotp is null)
        {
            return CodigoNoValido;
        }

        var ahora = reloj.GetUtcNow();
        var paso = Totp.Verificar(Base32.Decodificar(Protector.Unprotect(usuario.SecretoTotp)), LimpiarCodigo(codigo), ahora, usuario.TotpUltimoPaso);
        if (paso is null)
        {
            await RegistrarFalloAsync(baseDeDatos, usuario, ahora.UtcDateTime, cancelacion);
            return CodigoNoValido;
        }

        usuario.TotpUltimoPaso = paso;
        usuario.IntentosFallidos = 0;
        usuario.BloqueoHasta = null;
        usuario.SelloSeguridad = Guid.NewGuid();
        if (!await GuardarSinPisarseAsync(cancelacion))
        {
            return CodigoNoValido;
        }

        return new IdentidadDeSesion(usuario.Uuid, usuario.UuidFirmas);
    }

    public async Task<Resultado<ResultadoDeRecuperacion>> VerificarRecuperacionAsync(string codigo, CancellationToken cancelacion)
    {
        var usuario = await UsuarioDeLaSesionAsync(cancelacion);
        if (usuario is null || !usuario.SegundoFactorActivo)
        {
            return CodigoNoValido;
        }

        var ahora = reloj.GetUtcNow();
        var guardados = usuario.CodigosRecuperacionHash is null
            ? []
            : JsonSerializer.Deserialize<List<string>>(usuario.CodigosRecuperacionHash) ?? [];
        var buscado = Hash(NormalizarCodigoDeRecuperacion(codigo));
        var indice = guardados.FindIndex(g => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(g), Encoding.ASCII.GetBytes(buscado)));
        if (indice < 0)
        {
            await RegistrarFalloAsync(baseDeDatos, usuario, ahora.UtcDateTime, cancelacion);
            return CodigoNoValido;
        }

        guardados.RemoveAt(indice);
        usuario.CodigosRecuperacionHash = JsonSerializer.Serialize(guardados);
        usuario.IntentosFallidos = 0;
        usuario.BloqueoHasta = null;
        usuario.SelloSeguridad = Guid.NewGuid();
        if (!await GuardarSinPisarseAsync(cancelacion))
        {
            return CodigoNoValido;
        }

        return new ResultadoDeRecuperacion(new IdentidadDeSesion(usuario.Uuid, usuario.UuidFirmas), guardados.Count);
    }

    public async Task<Resultado<PerfilDeUsuario>> ObtenerPerfilAsync(CancellationToken cancelacion)
    {
        var usuarioId = sesion.UuidUsuarios;
        var usuario = await baseDeDatos.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Uuid == usuarioId, cancelacion);
        if (usuario is null || !usuario.Activo)
        {
            return Error.NoAutorizado("sin_sesion", "Necesitas ingresar para continuar.");
        }

        var firmaId = usuario.UuidFirmas;
        var firma = await baseDeDatos.Firmas.AsNoTracking().FirstOrDefaultAsync(f => f.Uuid == firmaId, cancelacion);
        return new PerfilDeUsuario(usuario.Nombre, usuario.Correo, usuario.Rol, firma?.Nombre ?? string.Empty);
    }

    internal static string Normalizar(string? correo) => (correo ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>El usuario de la sesión de la petición, si existe, está activo y no está bloqueado.</summary>
    private async Task<Usuario?> UsuarioDeLaSesionAsync(CancellationToken cancelacion)
    {
        var usuarioId = sesion.UuidUsuarios;
        var usuario = await baseDeDatos.Usuarios.FirstOrDefaultAsync(u => u.Uuid == usuarioId, cancelacion);
        if (usuario is null || !usuario.Activo || usuario.BloqueoHasta > reloj.GetUtcNow().UtcDateTime)
        {
            return null;
        }

        return usuario;
    }

    private async Task RegistrarFalloAsync(FirmasDbContext db, Usuario usuario, DateTime ahora, CancellationToken cancelacion)
    {
        usuario.IntentosFallidos++;
        if (usuario.IntentosFallidos >= opciones.Value.IntentosMaximos)
        {
            usuario.BloqueoHasta = ahora.AddMinutes(opciones.Value.MinutosDeBloqueo);
            usuario.IntentosFallidos = 0;
        }

        usuario.SelloSeguridad = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(cancelacion);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otro intento simultáneo ya actualizó la fila: este intento igual se rechaza.
        }
    }

    /// <summary>Guarda el cambio; si otro intento simultáneo ya usó el mismo código, el segundo no entra.</summary>
    private async Task<bool> GuardarSinPisarseAsync(CancellationToken cancelacion)
    {
        try
        {
            await baseDeDatos.SaveChangesAsync(cancelacion);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static string LimpiarCodigo(string? codigo) =>
        (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string NormalizarCodigoDeRecuperacion(string? codigo) =>
        (codigo ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

    private static List<string> GenerarCodigosDeRecuperacion() =>
        [.. Enumerable.Range(0, CantidadDeCodigosDeRecuperacion).Select(_ => GenerarCodigoDeRecuperacion())];

    private static string GenerarCodigoDeRecuperacion()
    {
        var caracteres = new char[LargoDeCodigoDeRecuperacion];
        for (var i = 0; i < caracteres.Length; i++)
        {
            caracteres[i] = AlfabetoDeRecuperacion[RandomNumberGenerator.GetInt32(AlfabetoDeRecuperacion.Length)];
        }

        return new string(caracteres);
    }

    /// <summary>Muestra el código en dos grupos de cinco: <c>ABCDE-FGHJK</c>.</summary>
    private static string DarFormato(string codigo) =>
        string.Create(CultureInfo.InvariantCulture, $"{codigo[..5]}-{codigo[5..]}");

    private static string Hash(string codigoNormalizado) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codigoNormalizado)));
}
