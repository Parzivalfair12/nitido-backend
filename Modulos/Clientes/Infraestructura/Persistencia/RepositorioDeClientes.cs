using Microsoft.EntityFrameworkCore;
using Nitido.Clientes.Aplicacion.Clientes;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Clientes.Infraestructura.Persistencia;

/// <summary>
/// Escritura de clientes. El cliente y su primer acceso se guardan en un solo <c>SaveChanges</c> (una transacción):
/// los predicados AFTER INSERT de las dos tablas lo permiten porque ambas filas son de la firma de la sesión.
/// </summary>
internal sealed class RepositorioDeClientes(ClientesDbContext contexto) : IRepositorioDeClientes
{
    public async Task<bool> AgregarAsync(Cliente cliente, CancellationToken cancelacion)
    {
        contexto.Clientes.Add(cliente);
        try
        {
            await contexto.SaveChangesAsync(cancelacion);
            return true;
        }
        catch (DbUpdateException ex) when (ViolacionDeUnicidad.Es(ex))
        {
            contexto.ChangeTracker.Clear();
            return false;
        }
    }
}

/// <summary>
/// Lectura de clientes. Filtra por los accesos del usuario de la sesión, además del filtro de EF (que es solo por
/// firma), para que las dos barreras digan lo mismo (ADR 0003). Ordenada por razón social.
/// </summary>
internal sealed class ConsultasDeClientes(ClientesDbContext contexto, IContextoSesion sesion) : IConsultasDeClientes
{
    public async Task<IReadOnlyList<ClienteDto>> ListarAsync(CancellationToken cancelacion)
    {
        var usuario = sesion.UuidUsuarios;
        return await contexto.Clientes
            .AsNoTracking()
            .Where(c => c.Accesos.Any(a => a.UuidUsuarios == usuario))
            .OrderBy(c => c.RazonSocial)
            .ThenBy(c => c.Uuid)
            .Select(c => new ClienteDto(c.Uuid, c.TipoDocumento, c.NumeroDocumento, c.DigitoVerificacion, c.RazonSocial, c.Activo))
            .ToListAsync(cancelacion);
    }

    public async Task<ClienteDto?> ObtenerAsync(Guid uuid, CancellationToken cancelacion)
    {
        var usuario = sesion.UuidUsuarios;
        return await contexto.Clientes
            .AsNoTracking()
            .Where(c => c.Uuid == uuid && c.Accesos.Any(a => a.UuidUsuarios == usuario))
            .Select(c => new ClienteDto(c.Uuid, c.TipoDocumento, c.NumeroDocumento, c.DigitoVerificacion, c.RazonSocial, c.Activo))
            .FirstOrDefaultAsync(cancelacion);
    }
}
