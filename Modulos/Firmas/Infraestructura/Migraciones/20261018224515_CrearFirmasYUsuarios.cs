using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nitido.Firmas.Infraestructura.Migraciones
{
    /// <inheritdoc />
    public partial class CrearFirmasYUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_FIRMAS",
                columns: table => new
                {
                    UUID_FIRMAS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NOMBRE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_T_FIRMAS", x => x.UUID_FIRMAS);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_FIRMAS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");

            migrationBuilder.CreateTable(
                name: "T_USUARIOS",
                columns: table => new
                {
                    UUID_USUARIOS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UUID_FIRMAS = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NOMBRE = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CORREO = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CORREO_NORMALIZADO = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ROL = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_T_USUARIOS", x => x.UUID_USUARIOS);
                    table.CheckConstraint("CK_T_USUARIOS_ROL", "ROL IN ('ADMINISTRADOR')");
                    table.ForeignKey(
                        name: "FK_T_USUARIOS_T_FIRMAS",
                        column: x => x.UUID_FIRMAS,
                        principalTable: "T_FIRMAS",
                        principalColumn: "UUID_FIRMAS",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_USUARIOS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");

            migrationBuilder.CreateIndex(
                name: "IX_T_USUARIOS_UUID_FIRMAS",
                table: "T_USUARIOS",
                column: "UUID_FIRMAS");

            migrationBuilder.CreateIndex(
                name: "UX_T_USUARIOS_CORREO_NORMALIZADO",
                table: "T_USUARIOS",
                column: "CORREO_NORMALIZADO",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_USUARIOS")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_USUARIOS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");

            migrationBuilder.DropTable(
                name: "T_FIRMAS")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "T_FIRMAS_HISTORIAL")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PERIODO_FIN")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PERIODO_INICIO");
        }
    }
}
