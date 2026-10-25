using Microsoft.EntityFrameworkCore;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;

namespace Nitido.Firmas.Infraestructura.Persistencia;

/// <summary>Contexto de EF del módulo Firmas: <c>T_FIRMAS</c> y <c>T_USUARIOS</c>.</summary>
internal sealed class FirmasDbContext(DbContextOptions<FirmasDbContext> opciones) : DbContextNitido(opciones)
{
    public DbSet<Firma> Firmas => Set<Firma>();

    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Firma no implementa IDeFirma (su llave es la firma), así que la convención no la cubre. El filtro lee la
        // firma de la sesión de la instancia del contexto, nunca un valor capturado (ver DbContextNitido).
        modelBuilder.Entity<Firma>().HasQueryFilter(FiltroMultiempresa, f => f.Uuid == FirmaDeLaSesion);
    }
}
