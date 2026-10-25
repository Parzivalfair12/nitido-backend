namespace Nitido.Compartido.Multiempresa;

/// <summary>
/// Puerto que usa el filtro del cliente activo (<c>Nitido.Compartido.Api</c>) para preguntar si el usuario de la
/// sesión puede entrar a un cliente, sin ver ningún módulo. Lo implementa Clientes (ADR 0003, decisión 7).
/// </summary>
public interface IAccesoAClientes
{
    /// <summary>
    /// <c>true</c> si el cliente es de la firma de la sesión y el usuario de la sesión tiene acceso a él.
    /// </summary>
    Task<bool> PuedeEntrarAsync(Guid uuidEmpresas, CancellationToken ct);
}
