namespace Nitido.Compartido.Api.Multiempresa;

/// <summary>
/// Nombres de los reclamos de la cookie de sesión que leen <c>ContextoSesionHttp</c> y las pruebas. F0-03a los
/// emite al ingresar; si los emite un proyecto de Infraestructura que no ve este, los mueve a
/// <c>Nitido.Compartido/Multiempresa/</c>.
/// </summary>
public static class ReclamosDeSesion
{
    /// <summary>Reclamo con el usuario que ingresó.</summary>
    public const string UuidUsuarios = "uuid_usuarios";

    /// <summary>Reclamo con la firma del usuario que ingresó.</summary>
    public const string UuidFirmas = "uuid_firmas";
}
