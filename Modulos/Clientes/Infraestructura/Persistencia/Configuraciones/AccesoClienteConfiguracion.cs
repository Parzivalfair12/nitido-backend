using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Compartido.Infraestructura.Persistencia;

namespace Nitido.Clientes.Infraestructura.Persistencia.Configuraciones;

/// <summary>
/// Mapeo de <see cref="AccesoCliente"/> a <c>T_USUARIOS_EMPRESAS</c> (tabla temporal, historial
/// <c>T_USUARIOS_EMPRESAS_HISTORIAL</c>). La llave foránea <c>UUID_USUARIOS</c> hacia <c>T_USUARIOS</c>
/// (otro módulo) no se mapea aquí: la crea a mano la migración <c>CrearClientes</c>. La llave hacia
/// <c>T_EMPRESAS</c> la declara <see cref="ClienteConfiguracion"/>.
/// </summary>
internal sealed class AccesoClienteConfiguracion : IEntityTypeConfiguration<AccesoCliente>
{
    public void Configure(EntityTypeBuilder<AccesoCliente> builder)
    {
        builder.MapearATablaTemporal("T_USUARIOS_EMPRESAS");

        // La columna de la llave primaria es UUID_USUARIOS_EMPRESAS (la pone ConvencionDeNombres).
        builder.HasKey(a => a.Uuid);

        // Propiedad sombra UUID_EMPRESAS: la usa la relación Cliente.Accesos y el índice de abajo.
        builder.Property<Guid>("UuidEmpresas")
            .IsRequired();

        // UUID_FIRMAS: parte de la llave foránea compuesta hacia T_EMPRESAS (UUID_EMPRESAS, UUID_FIRMAS) y del
        // predicado de seguridad por fila (F0-02b).
        builder.Property(a => a.UuidFirmas)
            .IsRequired();

        // Única y empieza por UUID_EMPRESAS: regla de multiempresa y predicado de seguridad por fila (F0-02b).
        builder.HasIndex("UuidEmpresas", nameof(AccesoCliente.UuidUsuarios))
            .IsUnique()
            .HasDatabaseName("UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS");

        // Respalda la consulta «clientes a los que puede entrar este usuario».
        builder.HasIndex(a => a.UuidUsuarios)
            .HasDatabaseName("IX_T_USUARIOS_EMPRESAS_UUID_USUARIOS");
    }
}
