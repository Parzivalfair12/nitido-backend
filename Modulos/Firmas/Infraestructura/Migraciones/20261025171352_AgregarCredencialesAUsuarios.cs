using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nitido.Firmas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarCredencialesAUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BLOQUEO_HASTA",
                table: "T_USUARIOS",
                type: "datetime2(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CLAVE_HASH",
                table: "T_USUARIOS",
                type: "varchar(500)",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CODIGOS_RECUPERACION_HASH",
                table: "T_USUARIOS",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "INTENTOS_FALLIDOS",
                table: "T_USUARIOS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SECRETO_TOTP",
                table: "T_USUARIOS",
                type: "varchar(500)",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SEGUNDO_FACTOR_ACTIVO",
                table: "T_USUARIOS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SELLO_SEGURIDAD",
                table: "T_USUARIOS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<long>(
                name: "TOTP_ULTIMO_PASO",
                table: "T_USUARIOS",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // La política PS_FIRMAS está enlazada al esquema de T_USUARIOS (SCHEMABINDING) y SQL Server no deja quitar
            // columnas de una tabla enlazada; ALTER SECURITY POLICY tampoco permite soltar el enlace. Por eso se quitan los
            // predicados de T_USUARIOS y de su historial, se quitan las columnas y se ponen otra vez los predicados de la
            // política de Firmas (los mismos de db/seguridad-por-fila/0001-firmas-politica.sql).
            migrationBuilder.Sql(
                "ALTER SECURITY POLICY seguridad.PS_FIRMAS "
                + "DROP FILTER PREDICATE ON dbo.T_USUARIOS, "
                + "DROP BLOCK PREDICATE ON dbo.T_USUARIOS AFTER INSERT, "
                + "DROP BLOCK PREDICATE ON dbo.T_USUARIOS AFTER UPDATE, "
                + "DROP BLOCK PREDICATE ON dbo.T_USUARIOS BEFORE UPDATE, "
                + "DROP BLOCK PREDICATE ON dbo.T_USUARIOS BEFORE DELETE, "
                + "DROP FILTER PREDICATE ON dbo.T_USUARIOS_HISTORIAL;");

            migrationBuilder.DropColumn(
                name: "BLOQUEO_HASTA",
                table: "T_USUARIOS");

            migrationBuilder.DropColumn(
                name: "CLAVE_HASH",
                table: "T_USUARIOS");

            migrationBuilder.DropColumn(
                name: "CODIGOS_RECUPERACION_HASH",
                table: "T_USUARIOS");

            migrationBuilder.DropColumn(
                name: "INTENTOS_FALLIDOS",
                table: "T_USUARIOS");

            migrationBuilder.DropColumn(
                name: "SECRETO_TOTP",
                table: "T_USUARIOS");

            migrationBuilder.DropColumn(
                name: "SEGUNDO_FACTOR_ACTIVO",
                table: "T_USUARIOS");

            migrationBuilder.DropColumn(
                name: "SELLO_SEGURIDAD",
                table: "T_USUARIOS");

            migrationBuilder.DropColumn(
                name: "TOTP_ULTIMO_PASO",
                table: "T_USUARIOS");

            migrationBuilder.Sql(
                "ALTER SECURITY POLICY seguridad.PS_FIRMAS "
                + "ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS, "
                + "ADD BLOCK PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS AFTER INSERT, "
                + "ADD BLOCK PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS AFTER UPDATE, "
                + "ADD BLOCK PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS BEFORE UPDATE, "
                + "ADD BLOCK PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS BEFORE DELETE, "
                + "ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS_HISTORIAL;");
        }
    }
}
