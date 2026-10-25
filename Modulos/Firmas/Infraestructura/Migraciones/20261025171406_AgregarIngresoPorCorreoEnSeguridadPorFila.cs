using Microsoft.EntityFrameworkCore.Migrations;
using Nitido.Compartido.Infraestructura.Persistencia;

#nullable disable

namespace Nitido.Firmas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarIngresoPorCorreoEnSeguridadPorFila : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Escrito a mano (ADR 0003, «Ingreso»): cambia el predicado FILTER de T_USUARIOS para que, con la llave de
            // sesión CORREO_INGRESO, el ingreso vea solo la fila de su correo
            // (db/seguridad-por-fila/0003-firmas-ingreso-por-correo.sql, incrustado). EF parte el texto por las líneas GO.
            migrationBuilder.Sql(ScriptsSql.Leer(
                typeof(AgregarIngresoPorCorreoEnSeguridadPorFila).Assembly,
                "SeguridadPorFila.0003-firmas-ingreso-por-correo.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER SECURITY POLICY seguridad.PS_FIRMAS "
                + "ALTER FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS;");
            migrationBuilder.Sql("DROP FUNCTION seguridad.FN_USUARIOS_ES_VISIBLE;");
        }
    }
}
