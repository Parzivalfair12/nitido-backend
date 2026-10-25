using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nitido.Clientes.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class CrearClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_EMPRESAS",
                columns: table => new
                {
                    UUID_EMPRESAS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UUID_FIRMAS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TIPO_DOCUMENTO = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: false),
                    NUMERO_DOCUMENTO = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    DIGITO_VERIFICACION = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: false),
                    RAZON_SOCIAL = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ACTIVO = table.Column<bool>(type: "bit", nullable: false),
                    FECHA_CREACION = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    FECHA_MODIFICACION = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    PERIODO_FIN = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PERIODO_INICIO = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    UUID_USUARIOS_CREACION = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UUID_USUARIOS_MODIFICACION = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_EMPRESAS", x => x.UUID_EMPRESAS);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_EMPRESAS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");

            migrationBuilder.CreateTable(
                name: "T_USUARIOS_EMPRESAS",
                columns: table => new
                {
                    UUID_USUARIOS_EMPRESAS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UUID_USUARIOS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FECHA_CREACION = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    FECHA_MODIFICACION = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    PERIODO_FIN = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PERIODO_INICIO = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    UUID_EMPRESAS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UUID_USUARIOS_CREACION = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UUID_USUARIOS_MODIFICACION = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_USUARIOS_EMPRESAS", x => x.UUID_USUARIOS_EMPRESAS);
                    table.ForeignKey(
                        name: "FK_T_USUARIOS_EMPRESAS_T_EMPRESAS",
                        column: x => x.UUID_EMPRESAS,
                        principalTable: "T_EMPRESAS",
                        principalColumn: "UUID_EMPRESAS",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_USUARIOS_EMPRESAS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");

            migrationBuilder.CreateIndex(
                name: "UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO",
                table: "T_EMPRESAS",
                columns: new[] { "UUID_FIRMAS", "TIPO_DOCUMENTO", "NUMERO_DOCUMENTO" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_USUARIOS_EMPRESAS_UUID_USUARIOS",
                table: "T_USUARIOS_EMPRESAS",
                column: "UUID_USUARIOS");

            migrationBuilder.CreateIndex(
                name: "UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS",
                table: "T_USUARIOS_EMPRESAS",
                columns: new[] { "UUID_EMPRESAS", "UUID_USUARIOS" },
                unique: true);

            // Llaves foráneas entre módulos (ADR 0002, spec F0-02a 2.4): se crean a mano, por nombre de tabla,
            // porque este DbContext no mapea T_FIRMAS ni T_USUARIOS (son de Firmas). Requieren que la migración
            // CrearFirmasYUsuarios ya esté aplicada. Siempre ON DELETE NO ACTION.
            migrationBuilder.AddForeignKey(
                name: "FK_T_EMPRESAS_T_FIRMAS",
                table: "T_EMPRESAS",
                column: "UUID_FIRMAS",
                principalTable: "T_FIRMAS",
                principalColumn: "UUID_FIRMAS",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_T_USUARIOS_EMPRESAS_T_USUARIOS",
                table: "T_USUARIOS_EMPRESAS",
                column: "UUID_USUARIOS",
                principalTable: "T_USUARIOS",
                principalColumn: "UUID_USUARIOS",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Primero las llaves foráneas entre módulos, que se crearon a mano en Up.
            migrationBuilder.DropForeignKey(
                name: "FK_T_USUARIOS_EMPRESAS_T_USUARIOS",
                table: "T_USUARIOS_EMPRESAS");

            migrationBuilder.DropForeignKey(
                name: "FK_T_EMPRESAS_T_FIRMAS",
                table: "T_EMPRESAS");

            migrationBuilder.DropTable(
                name: "T_USUARIOS_EMPRESAS")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_USUARIOS_EMPRESAS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");

            migrationBuilder.DropTable(
                name: "T_EMPRESAS")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_EMPRESAS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");
        }
    }
}
