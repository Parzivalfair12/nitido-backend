using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Nitido.Compartido.Infraestructura.Persistencia;

#nullable disable

namespace Nitido.Clientes.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarSeguridadPorFilaDeClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Escrito a mano (ADR 0003, spec F0-02b 2.3). EF solo generaba la columna NOT NULL con valor por defecto
            // Guid.Empty, que fallaría contra la llave foránea compuesta si ya hay accesos. Aquí la columna se agrega
            // como nula, se rellena desde la firma del cliente (la firma de una empresa no cambia nunca) y recién
            // entonces pasa a NOT NULL, con el versionado del sistema apagado para tocar también el historial.
            // Todo antes de crear la política: después, esta migración (sin sesión) no vería ninguna fila.
            // Cada migrationBuilder.Sql es un lote aparte: el UPDATE necesita que la columna ya exista.
            migrationBuilder.Sql("ALTER TABLE dbo.T_USUARIOS_EMPRESAS SET (SYSTEM_VERSIONING = OFF);");
            migrationBuilder.Sql("ALTER TABLE dbo.T_USUARIOS_EMPRESAS ADD UUID_FIRMAS UNIQUEIDENTIFIER NULL;");
            migrationBuilder.Sql("ALTER TABLE dbo.T_USUARIOS_EMPRESAS_HISTORIAL ADD UUID_FIRMAS UNIQUEIDENTIFIER NULL;");
            migrationBuilder.Sql(
                """
                UPDATE ACCESO
                SET ACCESO.UUID_FIRMAS = EMPRESA.UUID_FIRMAS
                FROM dbo.T_USUARIOS_EMPRESAS AS ACCESO
                JOIN dbo.T_EMPRESAS AS EMPRESA ON EMPRESA.UUID_EMPRESAS = ACCESO.UUID_EMPRESAS;
                """);

            // El historial puede tener accesos de un cliente que ya no está en T_EMPRESAS (borrado después de quitar
            // sus accesos): para esos se toma la firma del historial del cliente. Nunca se adivina una firma; si
            // alguna fila quedara sin firma, el paso a NOT NULL falla y deshace la migración.
            migrationBuilder.Sql(
                """
                UPDATE ACCESO
                SET ACCESO.UUID_FIRMAS = COALESCE(
                    (SELECT TOP (1) ACTUAL.UUID_FIRMAS FROM dbo.T_EMPRESAS AS ACTUAL WHERE ACTUAL.UUID_EMPRESAS = ACCESO.UUID_EMPRESAS),
                    (SELECT TOP (1) ANTERIOR.UUID_FIRMAS FROM dbo.T_EMPRESAS_HISTORIAL AS ANTERIOR WHERE ANTERIOR.UUID_EMPRESAS = ACCESO.UUID_EMPRESAS))
                FROM dbo.T_USUARIOS_EMPRESAS_HISTORIAL AS ACCESO;
                """);
            migrationBuilder.Sql("ALTER TABLE dbo.T_USUARIOS_EMPRESAS ALTER COLUMN UUID_FIRMAS UNIQUEIDENTIFIER NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE dbo.T_USUARIOS_EMPRESAS_HISTORIAL ALTER COLUMN UUID_FIRMAS UNIQUEIDENTIFIER NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE dbo.T_USUARIOS_EMPRESAS SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.T_USUARIOS_EMPRESAS_HISTORIAL));");

            // Llave alterna y llave foránea compuesta (conserva el nombre de la de F0-02a).
            migrationBuilder.DropForeignKey(
                name: "FK_T_USUARIOS_EMPRESAS_T_EMPRESAS",
                table: "T_USUARIOS_EMPRESAS");

            migrationBuilder.AddUniqueConstraint(
                name: "UX_T_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS",
                table: "T_EMPRESAS",
                columns: new[] { "UUID_EMPRESAS", "UUID_FIRMAS" });

            migrationBuilder.CreateIndex(
                name: "IX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS",
                table: "T_USUARIOS_EMPRESAS",
                columns: new[] { "UUID_EMPRESAS", "UUID_FIRMAS" });

            migrationBuilder.AddForeignKey(
                name: "FK_T_USUARIOS_EMPRESAS_T_EMPRESAS",
                table: "T_USUARIOS_EMPRESAS",
                columns: new[] { "UUID_EMPRESAS", "UUID_FIRMAS" },
                principalTable: "T_EMPRESAS",
                principalColumns: new[] { "UUID_EMPRESAS", "UUID_FIRMAS" },
                onDelete: ReferentialAction.NoAction);

            // Política PS_CLIENTES y sus tres funciones (db/seguridad-por-fila/0002-clientes-politica.sql, incrustado).
            // El esquema `seguridad` lo creó la migración de Firmas.
            migrationBuilder.Sql(ScriptsSql.Leer(typeof(AgregarSeguridadPorFilaDeClientes).Assembly, "SeguridadPorFila.0002-clientes-politica.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Orden inverso al Up. La política y las funciones van primero: SCHEMABINDING impide quitar la columna
            // UUID_FIRMAS mientras un predicado la use.
            migrationBuilder.Sql("DROP SECURITY POLICY seguridad.PS_CLIENTES;");
            migrationBuilder.Sql("DROP FUNCTION seguridad.FN_CLIENTES_ES_CLIENTE_PERMITIDO;");
            migrationBuilder.Sql("DROP FUNCTION seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO;");
            migrationBuilder.Sql("DROP FUNCTION seguridad.FN_CLIENTES_ES_DE_LA_FIRMA;");

            migrationBuilder.DropForeignKey(
                name: "FK_T_USUARIOS_EMPRESAS_T_EMPRESAS",
                table: "T_USUARIOS_EMPRESAS");

            migrationBuilder.DropIndex(
                name: "IX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS",
                table: "T_USUARIOS_EMPRESAS");

            migrationBuilder.DropUniqueConstraint(
                name: "UX_T_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS",
                table: "T_EMPRESAS");

            migrationBuilder.DropColumn(
                name: "UUID_FIRMAS",
                table: "T_USUARIOS_EMPRESAS");

            migrationBuilder.AddForeignKey(
                name: "FK_T_USUARIOS_EMPRESAS_T_EMPRESAS",
                table: "T_USUARIOS_EMPRESAS",
                column: "UUID_EMPRESAS",
                principalTable: "T_EMPRESAS",
                principalColumn: "UUID_EMPRESAS",
                onDelete: ReferentialAction.NoAction);
        }
    }
}
