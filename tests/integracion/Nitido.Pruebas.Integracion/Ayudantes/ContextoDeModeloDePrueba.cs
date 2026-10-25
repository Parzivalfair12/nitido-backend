using Microsoft.EntityFrameworkCore;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>Entidad de prueba que pertenece a un cliente (<see cref="IDeEmpresa"/>). Tabla <c>T_PRUEBAS_EMPRESA</c>.</summary>
internal sealed class PruebaDeEmpresa : IDeEmpresa
{
    public Guid Uuid { get; set; }

    public Guid UuidEmpresas { get; set; }

    public string Nombre { get; set; } = string.Empty;
}

/// <summary>
/// Entidad de prueba con las dos interfaces: es de una firma y de un cliente. Tabla <c>T_PRUEBAS_FIRMA_EMPRESA</c>.
/// </summary>
internal sealed class PruebaDeFirmaYEmpresa : IDeFirma, IDeEmpresa
{
    public Guid Uuid { get; set; }

    public Guid UuidFirmas { get; set; }

    public Guid UuidEmpresas { get; set; }

    public string Nombre { get; set; } = string.Empty;
}

/// <summary>
/// Un <see cref="DbContextNitido"/> solo de pruebas, con una entidad <see cref="IDeEmpresa"/> y otra con las
/// dos interfaces. Ninguna entidad real implementa todavía <see cref="IDeEmpresa"/> (llega en la fase 1), así
/// que esta es la forma de probar esa rama de la convención del filtro global. No lo registra nadie en el host.
/// </summary>
/// <remarks>
/// No declara ninguna <c>IEntityTypeConfiguration</c> en este ensamblado a propósito: la clase base aplica
/// todas las del ensamblado del contexto derivado, y las de otras pruebas se colarían en este modelo.
/// </remarks>
internal sealed class ContextoDeModeloDePrueba(DbContextOptions<ContextoDeModeloDePrueba> opciones) : DbContextNitido(opciones)
{
    public DbSet<PruebaDeEmpresa> PruebasDeEmpresa => Set<PruebaDeEmpresa>();

    public DbSet<PruebaDeFirmaYEmpresa> PruebasDeFirmaYEmpresa => Set<PruebaDeFirmaYEmpresa>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Las llaves y las tablas se declaran antes de la clase base: el filtro por convención se aplica al
        // final de su OnModelCreating, sobre las entidades que ya existen.
        modelBuilder.Entity<PruebaDeEmpresa>(e =>
        {
            e.ToTable("T_PRUEBAS_EMPRESA");
            e.HasKey(x => x.Uuid);
            e.Property(x => x.Nombre).HasMaxLength(50);
        });
        modelBuilder.Entity<PruebaDeFirmaYEmpresa>(e =>
        {
            e.ToTable("T_PRUEBAS_FIRMA_EMPRESA");
            e.HasKey(x => x.Uuid);
            e.Property(x => x.Nombre).HasMaxLength(50);
        });

        base.OnModelCreating(modelBuilder);
    }
}
