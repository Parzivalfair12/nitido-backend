using Microsoft.EntityFrameworkCore.Migrations;
using Nitido.Compartido.Infraestructura.Persistencia;

#nullable disable

namespace Nitido.Firmas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarSeguridadPorFilaDeFirmas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_T_FIRMAS_UUID_FIRMAS",
                table: "T_FIRMAS",
                sql: "UUID_FIRMAS <> '00000000-0000-0000-0000-000000000000'");

            // Escrito a mano (ADR 0003, spec F0-02b 5.3): crea el esquema `seguridad`, la función y la política
            // PS_FIRMAS (db/seguridad-por-fila/0001-firmas-politica.sql, incrustado). EF parte el texto en lotes por
            // las líneas GO.
            migrationBuilder.Sql(ScriptsSql.Leer(typeof(AgregarSeguridadPorFilaDeFirmas).Assembly, "SeguridadPorFila.0001-firmas-politica.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Orden inverso al Up. Si Clientes sigue aplicado, el esquema `seguridad` todavía tiene sus funciones y
            // DROP SCHEMA falla: EF deshace la migración entera (hay que bajar primero Clientes).
            migrationBuilder.Sql("DROP SECURITY POLICY seguridad.PS_FIRMAS;");
            migrationBuilder.Sql("DROP FUNCTION seguridad.FN_FIRMAS_ES_DE_LA_FIRMA;");
            migrationBuilder.Sql("DROP SCHEMA seguridad;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_T_FIRMAS_UUID_FIRMAS",
                table: "T_FIRMAS");
        }
    }
}
