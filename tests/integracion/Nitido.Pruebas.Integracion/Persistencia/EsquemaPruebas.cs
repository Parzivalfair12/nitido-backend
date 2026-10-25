namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Revisa el esquema real que dejan las migraciones (base compartida migrada una sola vez). Cada prueba
/// exige primero que existan las cuatro tablas de la historia, para no pasar en vacío.
/// </summary>
public class EsquemaPruebas(ContenedorSqlServer contenedor)
{
    private static readonly string[] TablasDeLaHistoria = ["T_FIRMAS", "T_USUARIOS", "T_EMPRESAS", "T_USUARIOS_EMPRESAS"];

    private async Task<string> BaseMigradaConLasCuatroTablasAsync(CancellationToken token)
    {
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        var tablas = await VerificadorDeEsquema.ListarTablasAsync(cadena, token);
        foreach (var tabla in TablasDeLaHistoria)
        {
            Assert.True(
                tablas.DeNitido.Contains(tabla),
                $"Falta la tabla {tabla}; la prueba de esquema no tiene qué revisar. Existen: [{string.Join(", ", tablas.Todas)}].");
        }

        return cadena;
    }

    [Fact]
    public async Task Esquema_SigueConvenciones()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseMigradaConLasCuatroTablasAsync(token);

        var infracciones = await VerificadorDeEsquema.InfraccionesDeConvencionesAsync(cadena, token);

        Assert.True(infracciones.Count == 0, "El esquema rompe convenciones-bd:\n- " + string.Join("\n- ", infracciones));
    }

    [Fact]
    public async Task Esquema_NoUsaFloatRealNiMoney()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseMigradaConLasCuatroTablasAsync(token);

        var prohibidas = await VerificadorDeEsquema.ColumnasConTiposProhibidosAsync(cadena, token);

        Assert.True(prohibidas.Count == 0, "Hay columnas con tipos prohibidos:\n- " + string.Join("\n- ", prohibidas));
    }

    [Fact]
    public async Task Esquema_TablasTemporalesTienenHistorial()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseMigradaConLasCuatroTablasAsync(token);

        foreach (var tabla in TablasDeLaHistoria)
        {
            var historial = $"{tabla}_HISTORIAL";

            // temporal_type 2 = tabla temporal con versionado del sistema; 1 = su historial.
            var filas = await ConsultasSql.FilasAsync(
                cadena,
                """
                SELECT t.temporal_type, SCHEMA_NAME(t.schema_id), h.name, SCHEMA_NAME(h.schema_id), h.temporal_type
                FROM sys.tables t
                LEFT JOIN sys.tables h ON h.object_id = t.history_table_id
                WHERE t.name = @tabla
                """,
                token,
                ("@tabla", tabla));
            var fila = Assert.Single(filas);
            Assert.True(Convert.ToInt32(fila[0]) == 2, $"{tabla} no es una tabla temporal (SYSTEM_VERSIONING = ON).");
            Assert.Equal(historial, fila[2]);
            Assert.Equal("dbo", fila[1]);
            Assert.Equal("dbo", fila[3]);
            Assert.Equal(1, Convert.ToInt32(fila[4]));

            // Periodos PERIODO_INICIO / PERIODO_FIN: datetime2(3), generados por SQL Server y OCULTOS.
            // Decisión del usuario (2026-10-18), ya recogida en la spec 2.3: los periodos son «ocultos» (HIDDEN), como
            // los crea EF Core 10 (propiedades sombra). Se exige is_hidden = 1 a propósito: si un día EF 11
            // los deja visibles, esta prueba avisa para revisar la decisión (y SELECT * del historial cambia).
            var periodos = await ConsultasSql.FilasAsync(
                cadena,
                """
                SELECT c.name, ty.name, c.generated_always_type, c.is_hidden, c.scale
                FROM sys.columns c
                JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID(@tabla) AND c.name IN ('PERIODO_INICIO', 'PERIODO_FIN')
                ORDER BY c.name
                """,
                token,
                ("@tabla", "dbo." + tabla));
            Assert.Equal(2, periodos.Count);
            var fin = periodos.Single(p => (string)p[0]! == "PERIODO_FIN");
            var inicio = periodos.Single(p => (string)p[0]! == "PERIODO_INICIO");
            Assert.Equal("datetime2", inicio[1]);
            Assert.Equal("datetime2", fin[1]);
            Assert.Equal(1, Convert.ToInt32(inicio[2])); // AS ROW START
            Assert.Equal(2, Convert.ToInt32(fin[2])); // AS ROW END
            Assert.Equal(3, Convert.ToInt32(inicio[4]));
            Assert.Equal(3, Convert.ToInt32(fin[4]));
            Assert.True(
                Convert.ToBoolean(inicio[3]) && Convert.ToBoolean(fin[3]),
                $"Los periodos de {tabla} deberían estar ocultos (HIDDEN, como los crea EF Core 10; decisión del 2026-10-18). "
                + "Si ahora son visibles, EF cambió su comportamiento: revisar la decisión.");

            var periodoDeSistema = await ConsultasSql.EscalarAsync(
                cadena,
                """
                SELECT COUNT(*) FROM sys.periods p
                JOIN sys.columns s ON s.object_id = p.object_id AND s.column_id = p.start_column_id
                JOIN sys.columns e ON e.object_id = p.object_id AND e.column_id = p.end_column_id
                WHERE p.object_id = OBJECT_ID(@tabla) AND p.name = 'SYSTEM_TIME'
                  AND s.name = 'PERIODO_INICIO' AND e.name = 'PERIODO_FIN'
                """,
                token,
                ("@tabla", "dbo." + tabla));
            Assert.Equal(1, Convert.ToInt32(periodoDeSistema));

            // El historial tiene las mismas columnas de la tabla.
            var distintas = await ConsultasSql.EscalarAsync(
                cadena,
                """
                SELECT COUNT(*) FROM (
                  SELECT c.name, c.system_type_id, c.max_length, c.is_nullable FROM sys.columns c WHERE c.object_id = OBJECT_ID(@tabla)
                  EXCEPT
                  SELECT c.name, c.system_type_id, c.max_length, c.is_nullable FROM sys.columns c WHERE c.object_id = OBJECT_ID(@historial)
                ) x
                """,
                token,
                ("@tabla", "dbo." + tabla),
                ("@historial", "dbo." + historial));
            Assert.Equal(0, Convert.ToInt32(distintas));
        }
    }

    [Fact]
    public async Task Esquema_CoincideConElDiseno()
    {
        // Columnas, índices, llaves foráneas y CHECK exactos de la sección 2.2 del diseño técnico de F0-02a, con
        // los cambios de la sección 2.2 de F0-02b: T_USUARIOS_EMPRESAS.UUID_FIRMAS, la llave alterna
        // (UUID_EMPRESAS, UUID_FIRMAS) de T_EMPRESAS, la llave foránea compuesta y CK_T_FIRMAS_UUID_FIRMAS.
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseMigradaConLasCuatroTablasAsync(token);

        (string Nombre, string Tipo, int? Largo, bool Nulo, int? Precision) C(string n, string t, int? l = null, bool nulo = false, int? p = null) =>
            (n, t, l, nulo, p);

        (string Nombre, string Tipo, int? Largo, bool Nulo, int? Precision)[] Comunes(params (string, string, int?, bool, int?)[] propias) =>
        [
            .. propias,
            C("FECHA_CREACION", "datetime2", p: 3),
            C("UUID_USUARIOS_CREACION", "uniqueidentifier"),
            C("FECHA_MODIFICACION", "datetime2", nulo: true, p: 3),
            C("UUID_USUARIOS_MODIFICACION", "uniqueidentifier", nulo: true),
            C("PERIODO_INICIO", "datetime2"),
            C("PERIODO_FIN", "datetime2"),
        ];

        var tablas = new Dictionary<string, (string Nombre, string Tipo, int? Largo, bool Nulo, int? Precision)[]>
        {
            ["T_FIRMAS"] = Comunes(C("UUID_FIRMAS", "uniqueidentifier"), C("NOMBRE", "nvarchar", 200)),
            ["T_USUARIOS"] = Comunes(
                C("UUID_USUARIOS", "uniqueidentifier"),
                C("UUID_FIRMAS", "uniqueidentifier"),
                C("NOMBRE", "nvarchar", 200),
                C("CORREO", "nvarchar", 256),
                C("CORREO_NORMALIZADO", "nvarchar", 256),
                C("ROL", "varchar", 20),
                C("ACTIVO", "bit"),
                // Credenciales y segundo factor (F0-03a y F0-03b).
                C("CLAVE_HASH", "varchar", 500, nulo: true),
                C("SELLO_SEGURIDAD", "uniqueidentifier"),
                C("INTENTOS_FALLIDOS", "int"),
                C("BLOQUEO_HASTA", "datetime2", nulo: true, p: 3),
                C("SEGUNDO_FACTOR_ACTIVO", "bit"),
                C("SECRETO_TOTP", "varchar", 500, nulo: true),
                C("TOTP_ULTIMO_PASO", "bigint", nulo: true),
                C("CODIGOS_RECUPERACION_HASH", "nvarchar", nulo: true)),
            ["T_EMPRESAS"] = Comunes(
                C("UUID_EMPRESAS", "uniqueidentifier"),
                C("UUID_FIRMAS", "uniqueidentifier"),
                C("TIPO_DOCUMENTO", "varchar", 4),
                C("NUMERO_DOCUMENTO", "varchar", 20),
                C("DIGITO_VERIFICACION", "char", 1),
                C("RAZON_SOCIAL", "nvarchar", 200),
                C("ACTIVO", "bit")),
            ["T_USUARIOS_EMPRESAS"] = Comunes(
                C("UUID_USUARIOS_EMPRESAS", "uniqueidentifier"),
                C("UUID_USUARIOS", "uniqueidentifier"),
                C("UUID_EMPRESAS", "uniqueidentifier"),
                C("UUID_FIRMAS", "uniqueidentifier")),
        };
        var problemas = new List<string>();
        foreach (var (tabla, columnasEsperadas) in tablas)
        {
            var reales = await ConsultasSql.FilasAsync(
                cadena,
                """
                SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, DATETIME_PRECISION
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @tabla
                """,
                token,
                ("@tabla", tabla));
            var porNombre = reales.ToDictionary(f => (string)f[0]!);

            foreach (var esperada in columnasEsperadas)
            {
                if (!porNombre.TryGetValue(esperada.Nombre, out var real))
                {
                    problemas.Add($"{tabla}: falta la columna {esperada.Nombre}.");
                    continue;
                }

                if ((string)real[1]! != esperada.Tipo)
                {
                    problemas.Add($"{tabla}.{esperada.Nombre}: es {real[1]} y debe ser {esperada.Tipo}.");
                }

                if (esperada.Largo is not null && Convert.ToInt32(real[2]) != esperada.Largo)
                {
                    problemas.Add($"{tabla}.{esperada.Nombre}: largo {real[2]} y debe ser {esperada.Largo} (nada de nvarchar(max)).");
                }

                if (((string)real[3]! == "YES") != esperada.Nulo)
                {
                    problemas.Add($"{tabla}.{esperada.Nombre}: " + (esperada.Nulo ? "debe admitir NULL." : "debe ser NOT NULL."));
                }

                if (esperada.Precision is not null && Convert.ToInt32(real[4]) != esperada.Precision)
                {
                    problemas.Add($"{tabla}.{esperada.Nombre}: precisión {real[4]} y debe ser {esperada.Precision}.");
                }
            }

            foreach (var sobrante in porNombre.Keys.Except(columnasEsperadas.Select(c => c.Nombre)))
            {
                problemas.Add($"{tabla}: columna no prevista en el diseño: {sobrante}.");
            }
        }

        // Sin valores por defecto, salvo los periodos (los genera SQL Server y no figuran como DEFAULT) y las tres
        // columnas de credenciales que se agregan a una tabla con filas (F0-03a): sus valores por defecto rellenan a
        // los usuarios que ya existían.
        var defectos = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT t.name, c.name FROM sys.default_constraints d
            JOIN sys.tables t ON t.object_id = d.parent_object_id
            JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
            WHERE t.name IN ('T_FIRMAS', 'T_USUARIOS', 'T_EMPRESAS', 'T_USUARIOS_EMPRESAS')
            """,
            token);
        string[] defectosPermitidos = ["T_USUARIOS.SELLO_SEGURIDAD", "T_USUARIOS.INTENTOS_FALLIDOS", "T_USUARIOS.SEGUNDO_FACTOR_ACTIVO"];
        problemas.AddRange(defectos
            .Where(f => !defectosPermitidos.Contains($"{f[0]}.{f[1]}"))
            .Select(f => $"{f[0]}.{f[1]}: tiene un valor por defecto y el diseño no lo prevé."));

        // Índices que no son llave primaria: nombre, unicidad y columnas en orden.
        var esperadosIndices = new[]
        {
            "T_USUARIOS|UX_T_USUARIOS_CORREO_NORMALIZADO|1|CORREO_NORMALIZADO",
            "T_USUARIOS|IX_T_USUARIOS_UUID_FIRMAS|0|UUID_FIRMAS",
            "T_EMPRESAS|UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO|1|UUID_FIRMAS,TIPO_DOCUMENTO,NUMERO_DOCUMENTO",
            "T_EMPRESAS|UX_T_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS|1|UUID_EMPRESAS,UUID_FIRMAS",
            "T_USUARIOS_EMPRESAS|UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS|1|UUID_EMPRESAS,UUID_USUARIOS",
            "T_USUARIOS_EMPRESAS|IX_T_USUARIOS_EMPRESAS_UUID_USUARIOS|0|UUID_USUARIOS",
            "T_USUARIOS_EMPRESAS|IX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS|0|UUID_EMPRESAS,UUID_FIRMAS",
        };
        var indices = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT t.name, i.name, i.is_unique, ic.key_ordinal, c.name
            FROM sys.tables t
            JOIN sys.indexes i ON i.object_id = t.object_id AND i.type > 0 AND i.is_primary_key = 0
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = ic.column_id
            WHERE t.name IN ('T_FIRMAS', 'T_USUARIOS', 'T_EMPRESAS', 'T_USUARIOS_EMPRESAS')
            ORDER BY t.name, i.name, ic.key_ordinal
            """,
            token);
        var realesIndices = indices
            .GroupBy(f => ((string)f[0]!, (string)f[1]!))
            .Select(g => $"{g.Key.Item1}|{g.Key.Item2}|{(Convert.ToBoolean(g.First()[2]) ? 1 : 0)}|{string.Join(',', g.Select(f => (string)f[4]!))}")
            .Order()
            .ToList();
        problemas.AddRange(esperadosIndices.Except(realesIndices).Select(i => $"Falta el índice (tabla|nombre|único|columnas) {i}."));
        problemas.AddRange(realesIndices.Except(esperadosIndices).Select(i => $"Índice no previsto en el diseño: {i}."));

        // Llaves foráneas, incluidas las dos entre módulos, sin cascada. La de T_USUARIOS_EMPRESAS hacia T_EMPRESAS
        // es compuesta (UUID_EMPRESAS, UUID_FIRMAS) desde F0-02b y conserva su nombre: aparece como dos pares.
        var esperadasFk = new[]
        {
            "FK_T_USUARIOS_T_FIRMAS|T_USUARIOS.UUID_FIRMAS->T_FIRMAS.UUID_FIRMAS",
            "FK_T_EMPRESAS_T_FIRMAS|T_EMPRESAS.UUID_FIRMAS->T_FIRMAS.UUID_FIRMAS",
            "FK_T_USUARIOS_EMPRESAS_T_EMPRESAS|T_USUARIOS_EMPRESAS.UUID_EMPRESAS->T_EMPRESAS.UUID_EMPRESAS",
            "FK_T_USUARIOS_EMPRESAS_T_EMPRESAS|T_USUARIOS_EMPRESAS.UUID_FIRMAS->T_EMPRESAS.UUID_FIRMAS",
            "FK_T_USUARIOS_EMPRESAS_T_USUARIOS|T_USUARIOS_EMPRESAS.UUID_USUARIOS->T_USUARIOS.UUID_USUARIOS",
        };
        var fks = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT fk.name, tp.name, cp.name, tr.name, cr.name, fk.delete_referential_action, fk.update_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
            JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
            JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
            JOIN sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
            WHERE tp.name IN ('T_FIRMAS', 'T_USUARIOS', 'T_EMPRESAS', 'T_USUARIOS_EMPRESAS')
            """,
            token);
        var realesFk = fks.Select(f => $"{f[0]}|{f[1]}.{f[2]}->{f[3]}.{f[4]}").Order().ToList();
        problemas.AddRange(esperadasFk.Except(realesFk).Select(i => $"Falta la llave foránea {i}."));
        problemas.AddRange(realesFk.Except(esperadasFk).Select(i => $"Llave foránea no prevista en el diseño: {i}."));
        problemas.AddRange(fks
            .Where(f => Convert.ToInt32(f[5]) != 0 || Convert.ToInt32(f[6]) != 0)
            .Select(f => $"{f[0]}: no debe tener acciones en cascada (ON DELETE/UPDATE NO ACTION)."));

        // Solo hay dos CHECK: el del rol (en la fase 0 solo admite ADMINISTRADOR) y el que impide una firma con
        // llave vacía (F0-02b, decisión 3: Guid.Empty significa «sin sesión»).
        var checks = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT t.name, cc.name, cc.definition FROM sys.check_constraints cc
            JOIN sys.tables t ON t.object_id = cc.parent_object_id
            WHERE t.name IN ('T_FIRMAS', 'T_USUARIOS', 'T_EMPRESAS', 'T_USUARIOS_EMPRESAS')
            """,
            token);
        var checksPorNombre = checks.ToDictionary(f => (string)f[1]!);
        if (checks.Count != 2)
        {
            problemas.Add($"Se esperaban exactamente dos CHECK (CK_T_USUARIOS_ROL y CK_T_FIRMAS_UUID_FIRMAS) y hay {checks.Count}: [{string.Join(", ", checksPorNombre.Keys)}].");
        }

        if (!checksPorNombre.TryGetValue("CK_T_USUARIOS_ROL", out var checkDelRol))
        {
            problemas.Add("Falta CK_T_USUARIOS_ROL.");
        }
        else if ((string)checkDelRol[0]! != "T_USUARIOS" || !((string)checkDelRol[2]!).Contains("ADMINISTRADOR", StringComparison.Ordinal))
        {
            problemas.Add($"CHECK inesperado: {checkDelRol[0]}.CK_T_USUARIOS_ROL = {checkDelRol[2]}.");
        }

        if (!checksPorNombre.TryGetValue("CK_T_FIRMAS_UUID_FIRMAS", out var checkDeLaFirma))
        {
            problemas.Add("Falta CK_T_FIRMAS_UUID_FIRMAS.");
        }
        else if ((string)checkDeLaFirma[0]! != "T_FIRMAS"
            || !((string)checkDeLaFirma[2]!).Contains("00000000-0000-0000-0000-000000000000", StringComparison.Ordinal))
        {
            problemas.Add($"CHECK inesperado: {checkDeLaFirma[0]}.CK_T_FIRMAS_UUID_FIRMAS = {checkDeLaFirma[2]}.");
        }

        Assert.True(problemas.Count == 0, "El esquema no coincide con el diseño 2.2:\n- " + string.Join("\n- ", problemas));
    }
}
