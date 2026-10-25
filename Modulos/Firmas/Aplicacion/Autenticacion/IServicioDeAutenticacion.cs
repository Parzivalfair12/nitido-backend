using Nitido.Compartido.Resultados;

namespace Nitido.Firmas.Aplicacion.Autenticacion;

/// <summary>Siguiente paso después de que la clave resultó correcta (el doble factor es obligatorio).</summary>
public enum PasoDeIngreso
{
    /// <summary>El usuario todavía no tiene doble factor: debe inscribir su aplicación autenticadora.</summary>
    InscribirSegundoFactor,

    /// <summary>El usuario ya tiene doble factor: debe escribir el código de su aplicación.</summary>
    SegundoFactor,
}

/// <summary>Quién es la persona que ingresó: lo único que viaja en la cookie de sesión.</summary>
public sealed record IdentidadDeSesion(Guid UuidUsuarios, Guid UuidFirmas);

public sealed record ResultadoDeIngreso(PasoDeIngreso Paso, IdentidadDeSesion Identidad);

/// <summary>Datos para registrar la aplicación autenticadora (TOTP, RFC 6238).</summary>
/// <param name="ClaveManual">Clave en base32, para escribirla a mano en la aplicación.</param>
/// <param name="UriOtpauth">URI <c>otpauth://totp/...</c>, que la pantalla convierte en código QR.</param>
public sealed record InscripcionDeSegundoFactor(string ClaveManual, string UriOtpauth);

public sealed record CodigosDeRecuperacionEmitidos(IdentidadDeSesion Identidad, IReadOnlyList<string> Codigos);

public sealed record ResultadoDeRecuperacion(IdentidadDeSesion Identidad, int CodigosRestantes);

public sealed record PerfilDeUsuario(string Nombre, string Correo, string Rol, string Firma);

/// <summary>
/// Puerto de la autenticación (F0-03a y F0-03b). Lo implementa Infraestructura con el hash de contraseñas de
/// Identity, el TOTP y Data Protection; la cookie la maneja el host. Las operaciones que siguen a la clave
/// (inscripción y códigos) actúan sobre el usuario de la sesión parcial de la petición.
/// </summary>
public interface IServicioDeAutenticacion
{
    /// <summary>Comprueba el correo y la clave. Cualquier falla devuelve el mismo error genérico.</summary>
    Task<Resultado<ResultadoDeIngreso>> IngresarAsync(string correo, string clave, CancellationToken cancelacion);

    /// <summary>Genera (o regenera, mientras no se confirme) la clave TOTP del usuario de la sesión parcial.</summary>
    Task<Resultado<InscripcionDeSegundoFactor>> ObtenerInscripcionAsync(CancellationToken cancelacion);

    /// <summary>Confirma la inscripción con un código de la aplicación y entrega los códigos de recuperación, una sola vez.</summary>
    Task<Resultado<CodigosDeRecuperacionEmitidos>> ConfirmarInscripcionAsync(string codigo, CancellationToken cancelacion);

    /// <summary>Verifica un código TOTP; con uno válido la persona queda con sesión completa.</summary>
    Task<Resultado<IdentidadDeSesion>> VerificarSegundoFactorAsync(string codigo, CancellationToken cancelacion);

    /// <summary>Verifica un código de recuperación (de un solo uso); con uno válido la persona queda con sesión completa.</summary>
    Task<Resultado<ResultadoDeRecuperacion>> VerificarRecuperacionAsync(string codigo, CancellationToken cancelacion);

    /// <summary>Perfil del usuario de la sesión (para <c>/yo</c>).</summary>
    Task<Resultado<PerfilDeUsuario>> ObtenerPerfilAsync(CancellationToken cancelacion);
}
