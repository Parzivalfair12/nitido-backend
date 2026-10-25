using Microsoft.EntityFrameworkCore;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Clientes.Infraestructura.Persistencia;

/// <summary>
/// Implementa <see cref="IAccesoAClientes"/>: el cliente existe, es de la firma de la sesión y el usuario de la
/// sesión tiene acceso a él. Pasa por las dos barreras: el filtro de EF (firma) más la condición explícita de
/// acceso, y la política de <c>T_EMPRESAS</c> (firma y acceso). Un cliente inactivo sigue siendo accesible.
/// </summary>
internal sealed class AccesoAClientes(ClientesDbContext contexto, IContextoSesion sesion) : IAccesoAClientes
{
    public async Task<bool> PuedeEntrarAsync(Guid uuidEmpresas, CancellationToken ct)
    {
        var uuidUsuarios = sesion.UuidUsuarios;
        return await contexto.Clientes
            .AsNoTracking()
            .AnyAsync(c => c.Uuid == uuidEmpresas && c.Accesos.Any(a => a.UuidUsuarios == uuidUsuarios), ct);
    }
}
