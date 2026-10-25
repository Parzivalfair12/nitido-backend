namespace Nitido.Compartido.Multiempresa;

/// <summary>
/// Entidad con datos de un cliente. El filtro global de EF la limita al cliente activo de la sesión
/// (<see cref="IContextoSesion.UuidEmpresas"/>).
/// </summary>
public interface IDeEmpresa
{
    /// <summary>Cliente dueño de la fila (columna <c>UUID_EMPRESAS</c>).</summary>
    Guid UuidEmpresas { get; }
}
