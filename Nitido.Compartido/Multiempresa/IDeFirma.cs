namespace Nitido.Compartido.Multiempresa;

/// <summary>
/// Entidad que pertenece a una firma. El filtro global de EF la limita a la firma de la sesión
/// (<see cref="IContextoSesion.UuidFirmas"/>).
/// </summary>
public interface IDeFirma
{
    /// <summary>Firma dueña de la fila (columna <c>UUID_FIRMAS</c>).</summary>
    Guid UuidFirmas { get; }
}
