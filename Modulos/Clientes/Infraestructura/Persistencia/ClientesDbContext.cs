using Microsoft.EntityFrameworkCore;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Compartido.Infraestructura.Persistencia;

namespace Nitido.Clientes.Infraestructura.Persistencia;

/// <summary>Contexto de EF del módulo Clientes: <c>T_EMPRESAS</c> y <c>T_USUARIOS_EMPRESAS</c>.</summary>
internal sealed class ClientesDbContext(DbContextOptions<ClientesDbContext> opciones) : DbContextNitido(opciones)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();

    public DbSet<AccesoCliente> Accesos => Set<AccesoCliente>();
}
