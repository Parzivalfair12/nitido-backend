using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Compartido.Infraestructura.Persistencia;

namespace Nitido.Clientes.Infraestructura.Persistencia.Configuraciones;

/// <summary>
/// Mapeo de <see cref="Cliente"/> a <c>T_EMPRESAS</c> (tabla temporal, historial <c>T_EMPRESAS_HISTORIAL</c>).
/// La llave foránea <c>UUID_FIRMAS</c> hacia <c>T_FIRMAS</c> (otro módulo) no se mapea aquí: la crea a mano
/// la migración <c>CrearClientes</c>.
/// </summary>
internal sealed class ClienteConfiguracion : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.MapearATablaTemporal("T_EMPRESAS");

        builder.HasKey(c => c.Uuid);

        builder.Property(c => c.TipoDocumento)
            .HasMaxLength(4)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(c => c.NumeroDocumento)
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(c => c.DigitoVerificacion)
            .HasMaxLength(1)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.Property(c => c.RazonSocial)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Activo)
            .IsRequired();

        builder.HasIndex(c => new { c.UuidFirmas, c.TipoDocumento, c.NumeroDocumento })
            .IsUnique()
            .HasDatabaseName("UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO");

        // Llave alterna para la llave foránea compuesta de los accesos (ADR 0003): ConvencionDeNombres no nombra
        // las llaves alternas, por eso el nombre va explícito.
        builder.HasAlternateKey(c => new { c.Uuid, c.UuidFirmas })
            .HasName("UX_T_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS");

        // Los accesos son hijos del cliente; la llave UUID_EMPRESAS es una propiedad sombra de la relación.
        // La llave foránea es compuesta (UUID_EMPRESAS, UUID_FIRMAS): un acceso no puede apuntar a un cliente de otra firma.
        builder.HasMany(c => c.Accesos)
            .WithOne()
            .HasForeignKey("UuidEmpresas", nameof(AccesoCliente.UuidFirmas))
            .HasPrincipalKey(c => new { c.Uuid, c.UuidFirmas })
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(c => c.Accesos)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
