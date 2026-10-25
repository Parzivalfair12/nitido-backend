using Microsoft.EntityFrameworkCore;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>Es de una firma (<see cref="IDeFirma"/>) y su contexto NO le puso el filtro.</summary>
internal sealed class IncumpleConInterfazDeFirma : IDeFirma
{
    public Guid Uuid { get; set; }

    public Guid UuidFirmas { get; set; }
}

/// <summary>Es de un cliente (<see cref="IDeEmpresa"/>) y su contexto NO le puso el filtro.</summary>
internal sealed class IncumpleConInterfazDeEmpresa : IDeEmpresa
{
    public Guid Uuid { get; set; }

    public Guid UuidEmpresas { get; set; }
}

/// <summary>Sin interfaz, pero con una columna <c>UUID_EMPRESAS</c> en sombra y sin filtro.</summary>
internal sealed class IncumpleConColumnaEnSombra
{
    public Guid Uuid { get; set; }
}

/// <summary>Como <c>Firma</c>: su llave primaria es <c>UUID_FIRMAS</c>, no tiene interfaz y no tiene filtro.</summary>
internal sealed class IncumpleConLlaveQueEsLaFirma
{
    public Guid UuidFirmas { get; set; }
}

/// <summary>Cumple: implementa <see cref="IDeFirma"/> y tiene el filtro con nombre.</summary>
internal sealed class CumpleConFiltroNombrado : IDeFirma
{
    public Guid Uuid { get; set; }

    public Guid UuidFirmas { get; set; }
}

/// <summary>Una entidad cualquiera, sin ninguna de las dos columnas: no necesita filtro.</summary>
internal sealed class SinColumnasDeMultiempresa
{
    public Guid Uuid { get; set; }
}

/// <summary>
/// Un contexto <b>mal armado a propósito</b> (no deriva de <see cref="DbContextNitido"/>, así que no recibe la
/// convención del filtro): sirve para demostrar que <see cref="VerificadorDeFiltros"/> detecta cada entidad sin
/// filtro. Si el verificador dejara de marcarlas, <c>FiltroGlobal_CubreTodaEntidadDeEmpresaYFirma</c> pasaría en vacío.
/// </summary>
internal sealed class ContextoDeModeloIncumplido(DbContextOptions<ContextoDeModeloIncumplido> opciones) : DbContext(opciones)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IncumpleConInterfazDeFirma>(e =>
        {
            e.ToTable("T_INCUMPLE_FIRMA");
            e.HasKey(x => x.Uuid);
            e.Property(x => x.UuidFirmas).HasColumnName("UUID_FIRMAS");
        });
        modelBuilder.Entity<IncumpleConInterfazDeEmpresa>(e =>
        {
            e.ToTable("T_INCUMPLE_EMPRESA");
            e.HasKey(x => x.Uuid);
            e.Property(x => x.UuidEmpresas).HasColumnName("UUID_EMPRESAS");
        });
        modelBuilder.Entity<IncumpleConColumnaEnSombra>(e =>
        {
            e.ToTable("T_INCUMPLE_SOMBRA");
            e.HasKey(x => x.Uuid);
            e.Property<Guid>("UuidEmpresas").HasColumnName("UUID_EMPRESAS");
        });
        modelBuilder.Entity<IncumpleConLlaveQueEsLaFirma>(e =>
        {
            e.ToTable("T_INCUMPLE_LLAVE");
            e.HasKey(x => x.UuidFirmas);
            e.Property(x => x.UuidFirmas).HasColumnName("UUID_FIRMAS");
        });
        modelBuilder.Entity<CumpleConFiltroNombrado>(e =>
        {
            e.ToTable("T_CUMPLE");
            e.HasKey(x => x.Uuid);
            e.Property(x => x.UuidFirmas).HasColumnName("UUID_FIRMAS");
            e.HasQueryFilter(DbContextNitido.FiltroMultiempresa, x => x.UuidFirmas != Guid.Empty);
        });
        modelBuilder.Entity<SinColumnasDeMultiempresa>(e =>
        {
            e.ToTable("T_NEUTRA");
            e.HasKey(x => x.Uuid);
        });
    }
}
