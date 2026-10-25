using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;

namespace Nitido.Firmas.Infraestructura.Persistencia.Configuraciones;

/// <summary>Mapeo de <see cref="Firma"/> a <c>T_FIRMAS</c> (tabla temporal, historial <c>T_FIRMAS_HISTORIAL</c>).</summary>
internal sealed class FirmaConfiguracion : IEntityTypeConfiguration<Firma>
{
    public void Configure(EntityTypeBuilder<Firma> builder)
    {
        builder.MapearATablaTemporal("T_FIRMAS");

        // Ninguna firma puede tener llave vacía: Guid.Empty significa «sin sesión» en SESSION_CONTEXT (ADR 0003).
        builder.ToTable(t => t.HasCheckConstraint("CK_T_FIRMAS_UUID_FIRMAS", "UUID_FIRMAS <> '00000000-0000-0000-0000-000000000000'"));

        builder.HasKey(f => f.Uuid);

        builder.Property(f => f.Nombre)
            .HasMaxLength(200)
            .IsRequired();
    }
}
