namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Prueba al verificador de esquema con una base armada a propósito: así, que las pruebas del esquema real
/// salgan sin infracciones significa que no hay infracciones, no que el verificador no ve nada.
/// No depende de las migraciones.
/// </summary>
public class VerificadorDeEsquemaPruebas(ContenedorSqlServer contenedor)
{
    private const string EsquemaDeEjemplo = """
        CREATE TABLE T_EMPRESAS (
          UUID_EMPRESAS UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_T_EMPRESAS PRIMARY KEY,
          FECHA_CREACION DATETIME2(3) NOT NULL, UUID_USUARIOS_CREACION UNIQUEIDENTIFIER NOT NULL,
          FECHA_MODIFICACION DATETIME2(3) NULL, UUID_USUARIOS_MODIFICACION UNIQUEIDENTIFIER NULL);

        -- Válida: temporal con historial (sin llave primaria ni llave foránea en el historial) y cliente bien hecho.
        CREATE TABLE T_CORRECTA (
          UUID_CORRECTA UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_T_CORRECTA PRIMARY KEY,
          UUID_EMPRESAS UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_T_CORRECTA_T_EMPRESAS REFERENCES T_EMPRESAS(UUID_EMPRESAS),
          FECHA_CREACION DATETIME2(3) NOT NULL, UUID_USUARIOS_CREACION UNIQUEIDENTIFIER NOT NULL,
          FECHA_MODIFICACION DATETIME2(3) NULL, UUID_USUARIOS_MODIFICACION UNIQUEIDENTIFIER NULL,
          PERIODO_INICIO DATETIME2(3) GENERATED ALWAYS AS ROW START NOT NULL,
          PERIODO_FIN DATETIME2(3) GENERATED ALWAYS AS ROW END NOT NULL,
          PERIOD FOR SYSTEM_TIME (PERIODO_INICIO, PERIODO_FIN))
          WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.T_CORRECTA_HISTORIAL));
        CREATE INDEX IX_T_CORRECTA_UUID_EMPRESAS ON T_CORRECTA (UUID_EMPRESAS);

        -- Válida: historial de migraciones de EF.
        CREATE TABLE __EFMigrationsHistory_Ejemplo (MigrationId NVARCHAR(150) NOT NULL PRIMARY KEY, ProductVersion NVARCHAR(32) NOT NULL);

        -- Infracciones.
        CREATE TABLE MALA (ID INT NOT NULL PRIMARY KEY, VALOR FLOAT NOT NULL);
        CREATE TABLE T_SIN_LLAVE (X INT NOT NULL);
        CREATE TABLE T_SIN_VINCULO (
          UUID_SIN_VINCULO UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_T_SIN_VINCULO PRIMARY KEY,
          UUID_EMPRESAS UNIQUEIDENTIFIER NOT NULL,
          DINERO MONEY NOT NULL, TASA REAL NOT NULL, MENUDO SMALLMONEY NOT NULL,
          FECHA_CREACION DATETIME2(7) NOT NULL, UUID_USUARIOS_CREACION UNIQUEIDENTIFIER NOT NULL);
        CREATE TABLE T_MAL_NOMBRADA (
          ID UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OTRO PRIMARY KEY,
          UUID_EMPRESAS UNIQUEIDENTIFIER NOT NULL CONSTRAINT FKX FOREIGN KEY REFERENCES T_EMPRESAS(UUID_EMPRESAS) ON DELETE CASCADE,
          ESTADO VARCHAR(5) NOT NULL CONSTRAINT UNO CHECK (ESTADO IN ('A')),
          FECHA_CREACION DATETIME2(3) NOT NULL, UUID_USUARIOS_CREACION UNIQUEIDENTIFIER NOT NULL,
          FECHA_MODIFICACION DATETIME2(3) NULL, UUID_USUARIOS_MODIFICACION UNIQUEIDENTIFIER NULL);
        CREATE INDEX MI_INDICE ON T_MAL_NOMBRADA (UUID_EMPRESAS);
        """;

    private async Task<string> BaseDeEjemploAsync(CancellationToken token)
    {
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        foreach (var lote in EsquemaDeEjemplo.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            await ConsultasSql.EjecutarAsync(cadena, lote, token);
        }

        return cadena;
    }

    [Fact]
    public async Task Verificador_DetectaCadaInfraccionYNoMarcaLoCorrecto()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseDeEjemploAsync(token);

        var infracciones = await VerificadorDeEsquema.InfraccionesDeConvencionesAsync(cadena, token);
        var texto = string.Join("\n", infracciones);

        string[] debenAparecer =
        [
            "MALA: una tabla de nítido debe empezar por T_.",
            "MALA: la llave primaria es (ID) y debe ser UUID_MALA.",
            "T_SIN_LLAVE: no tiene llave primaria.",
            "T_SIN_VINCULO: UUID_EMPRESAS no tiene llave foránea a T_EMPRESAS.",
            "T_SIN_VINCULO: no hay un índice que empiece por UUID_EMPRESAS.",
            "T_SIN_VINCULO: falta la columna de auditoría FECHA_MODIFICACION.",
            "T_SIN_VINCULO.FECHA_CREACION: tiene precisión 7 y debe ser DATETIME2(3).",
            "T_MAL_NOMBRADA: la llave primaria es (ID) y debe ser UUID_MAL_NOMBRADA.",
            "T_MAL_NOMBRADA: la restricción de llave primaria se llama PK_OTRO y debe ser PK_T_MAL_NOMBRADA.",
            "T_MAL_NOMBRADA: la llave foránea FKX debe llamarse FK_T_MAL_NOMBRADA_T_EMPRESAS",
            "T_MAL_NOMBRADA: la llave foránea FKX borra o anula en cascada",
            "T_MAL_NOMBRADA: el índice MI_INDICE debe llamarse IX_T_MAL_NOMBRADA_UUID_EMPRESAS.",
            "T_MAL_NOMBRADA: la restricción CHECK UNO debe llamarse CK_T_MAL_NOMBRADA_<COLUMNA>.",
        ];
        foreach (var esperada in debenAparecer)
        {
            Assert.True(texto.Contains(esperada, StringComparison.Ordinal), $"El verificador no detectó: «{esperada}». Infracciones:\n{texto}");
        }

        // Lo correcto y lo excluido no se marca: T_EMPRESAS, T_CORRECTA, su historial y el historial de EF.
        foreach (var inocente in new[] { "T_EMPRESAS:", "T_CORRECTA", "__EFMigrationsHistory" })
        {
            Assert.DoesNotContain(inocente, texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Verificador_DetectaFloatRealMoneyYSmallmoney()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseDeEjemploAsync(token);

        var prohibidas = await VerificadorDeEsquema.ColumnasConTiposProhibidosAsync(cadena, token);
        var texto = string.Join("\n", prohibidas);

        foreach (var columna in new[] { "MALA.VALOR", "T_SIN_VINCULO.DINERO", "T_SIN_VINCULO.TASA", "T_SIN_VINCULO.MENUDO" })
        {
            Assert.Contains($"{columna}:", texto, StringComparison.Ordinal);
        }

        Assert.Equal(4, prohibidas.Count);
    }

    [Fact]
    public async Task Verificador_ListaLasTablasSinMezclarHistoriales()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseDeEjemploAsync(token);

        var tablas = await VerificadorDeEsquema.ListarTablasAsync(cadena, token);

        Assert.Contains("T_CORRECTA", tablas.DeNitido);
        Assert.DoesNotContain("T_CORRECTA_HISTORIAL", tablas.DeNitido);
        Assert.Contains("T_CORRECTA_HISTORIAL", tablas.Historiales);
        Assert.DoesNotContain("__EFMigrationsHistory_Ejemplo", tablas.DeNitido);
        Assert.Contains("__EFMigrationsHistory_Ejemplo", tablas.Todas);
    }
}
