using Nitido.Compartido.Multiempresa;

namespace Nitido.Firmas.Infraestructura.Persistencia;

/// <summary>
/// Clase de persistencia de <c>T_USUARIOS</c>. Vive en Infraestructura y no en el dominio porque lleva las
/// credenciales y el estado del doble factor, que son detalles de la autenticación (F0-03a y F0-03b).
/// </summary>
internal sealed class Usuario : IDeFirma
{
    public Guid Uuid { get; set; }

    public Guid UuidFirmas { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Correo { get; set; } = string.Empty;

    public string CorreoNormalizado { get; set; } = string.Empty;

    public string Rol { get; set; } = string.Empty;

    public bool Activo { get; set; }

    /// <summary>Hash de la contraseña (<c>PasswordHasher</c> de Identity). Nunca la contraseña en claro.</summary>
    public string? ClaveHash { get; set; }

    /// <summary>
    /// Sello de seguridad: cambia cada vez que cambia el estado de las credenciales. También es la ficha de
    /// concurrencia de la fila, para que dos intentos simultáneos no se pisen los contadores ni reutilicen un código.
    /// </summary>
    public Guid SelloSeguridad { get; set; }

    public int IntentosFallidos { get; set; }

    /// <summary>Instante UTC hasta el que la cuenta está bloqueada, o <c>null</c> si no lo está.</summary>
    public DateTime? BloqueoHasta { get; set; }

    /// <summary>Indica si el usuario ya confirmó su aplicación autenticadora.</summary>
    public bool SegundoFactorActivo { get; set; }

    /// <summary>Secreto TOTP en base32, cifrado con Data Protection. Antes de confirmar la inscripción es el secreto pendiente.</summary>
    public string? SecretoTotp { get; set; }

    /// <summary>Último paso de tiempo TOTP aceptado: un código de ese paso (o anterior) no sirve otra vez.</summary>
    public long? TotpUltimoPaso { get; set; }

    /// <summary>Arreglo JSON con el SHA-256 (hexadecimal) de cada código de recuperación que no se ha usado.</summary>
    public string? CodigosRecuperacionHash { get; set; }
}
