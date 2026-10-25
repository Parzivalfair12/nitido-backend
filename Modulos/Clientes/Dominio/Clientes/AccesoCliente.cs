using Nitido.Compartido.Multiempresa;

namespace Nitido.Clientes.Dominio.Clientes;

/// <summary>
/// El acceso de un usuario a un cliente. Es hija de <see cref="Cliente"/>; la llave hacia el
/// cliente es una propiedad sombra de la relación y el dominio no la carga.
/// </summary>
public sealed class AccesoCliente : IDeFirma
{
    private AccesoCliente()
    {
    }

    public Guid Uuid { get; private set; }

    /// <summary>
    /// Firma del cliente al que da acceso. Repite el dato de <c>T_EMPRESAS</c>: una llave foránea compuesta
    /// (<c>UUID_EMPRESAS</c>, <c>UUID_FIRMAS</c>) impide que se desalinee (ADR 0003).
    /// </summary>
    public Guid UuidFirmas { get; private set; }

    public Guid UuidUsuarios { get; private set; }

    internal static AccesoCliente Crear(Guid uuidFirmas, Guid uuidUsuarios, DateTimeOffset ahora) =>
        new()
        {
            Uuid = Guid.CreateVersion7(ahora),
            UuidFirmas = uuidFirmas,
            UuidUsuarios = uuidUsuarios,
        };
}
