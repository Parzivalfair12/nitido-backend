using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;

namespace Nitido.Firmas.Infraestructura.Persistencia.Configuraciones;

/// <summary>Mapeo de <see cref="Usuario"/> a <c>T_USUARIOS</c> (tabla temporal, historial <c>T_USUARIOS_HISTORIAL</c>).</summary>
internal sealed class UsuarioConfiguracion : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.MapearATablaTemporal("T_USUARIOS");
        builder.ToTable(t => t.HasCheckConstraint("CK_T_USUARIOS_ROL", "ROL IN ('ADMINISTRADOR')"));

        builder.HasKey(u => u.Uuid);

        builder.Property(u => u.Nombre)
            .HasMaxLength(200)
            .IsRequired();

        // 256 es el largo que usa Identity para el correo (F0-03a mapea Email y NormalizedEmail aquí).
        builder.Property(u => u.Correo)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.CorreoNormalizado)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(u => u.Rol)
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(u => u.Activo)
            .IsRequired();

        builder.Property(u => u.ClaveHash)
            .HasMaxLength(500)
            .IsUnicode(false);

        // El valor por defecto solo rellena a los usuarios que ya existían al agregar la columna; toda fila nueva
        // trae su sello. Es además la ficha de concurrencia de la fila.
        builder.Property(u => u.SelloSeguridad)
            .HasDefaultValueSql("NEWID()")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(u => u.IntentosFallidos)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(u => u.BloqueoHasta);

        builder.Property(u => u.SegundoFactorActivo)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(u => u.SecretoTotp)
            .HasMaxLength(500)
            .IsUnicode(false);

        builder.Property(u => u.TotpUltimoPaso);

        builder.Property(u => u.CodigosRecuperacionHash);

        // Un correo pertenece a una sola firma: el índice es global, no por firma.
        builder.HasIndex(u => u.CorreoNormalizado)
            .IsUnique()
            .HasDatabaseName("UX_T_USUARIOS_CORREO_NORMALIZADO");

        // Llave foránea dentro del módulo (T_FIRMAS es de Firmas) y el índice que la respalda.
        builder.HasOne<Firma>()
            .WithMany()
            .HasForeignKey(u => u.UuidFirmas)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.UuidFirmas)
            .HasDatabaseName("IX_T_USUARIOS_UUID_FIRMAS");
    }
}
