namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Revisa un esquema de SQL Server contra <c>convenciones-bd</c> consultando <c>sys.*</c>. Devuelve la lista
/// de infracciones (vacía si todo cumple), de modo que las pruebas del esquema real exigen lista vacía y las
/// pruebas del verificador comprueban, con una base armada a propósito, que de verdad detecta cada infracción.
/// Excluye las tablas de historial de tablas temporales y las <c>__EFMigrationsHistory_*</c> (skill
/// <c>convenciones-bd</c>, «Migraciones»); las columnas de auditoría sí se revisan también en el historial.
/// </summary>
internal static class VerificadorDeEsquema
{
    private const string PrefijoHistorialDeMigraciones = "__EFMigrationsHistory_";

    private static readonly string[] ColumnasDeAuditoria =
        ["FECHA_CREACION", "UUID_USUARIOS_CREACION", "FECHA_MODIFICACION", "UUID_USUARIOS_MODIFICACION"];

    /// <summary>Tablas de nítido que se revisan (sin historiales ni historial de migraciones) y todas las de usuario.</summary>
    public sealed record Tablas(IReadOnlyList<string> DeNitido, IReadOnlyList<string> Historiales, IReadOnlyList<string> Todas);

    public static async Task<Tablas> ListarTablasAsync(string cadena, CancellationToken token)
    {
        var filas = await ConsultasSql.FilasAsync(
            cadena,
            "SELECT name, temporal_type FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY name",
            token);
        var todas = filas.Select(f => (string)f[0]!).ToList();
        var historiales = filas.Where(f => Convert.ToInt32(f[1]) == 1).Select(f => (string)f[0]!).ToList();
        var deNitido = filas
            .Where(f => Convert.ToInt32(f[1]) != 1)
            .Select(f => (string)f[0]!)
            .Where(n => !n.StartsWith(PrefijoHistorialDeMigraciones, StringComparison.Ordinal))
            .ToList();
        return new Tablas(deNitido, historiales, todas);
    }

    public static async Task<List<string>> InfraccionesDeConvencionesAsync(string cadena, CancellationToken token)
    {
        var infracciones = new List<string>();
        var tablas = await ListarTablasAsync(cadena, token);

        // 1. Toda tabla de nítido empieza por T_.
        foreach (var tabla in tablas.DeNitido.Where(t => !t.StartsWith("T_", StringComparison.Ordinal)))
        {
            infracciones.Add($"{tabla}: una tabla de nítido debe empezar por T_.");
        }

        // 2. Llave primaria de una sola columna UUID_<TABLA sin T_> y restricción PK_<TABLA>.
        var llaves = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT t.name, kc.name, c.name
            FROM sys.tables t
            JOIN sys.key_constraints kc ON kc.parent_object_id = t.object_id AND kc.type = 'PK'
            JOIN sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = ic.column_id
            ORDER BY t.name, ic.key_ordinal
            """,
            token);
        var llavesPorTabla = llaves.GroupBy(f => (string)f[0]!).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var tabla in tablas.DeNitido)
        {
            if (!llavesPorTabla.TryGetValue(tabla, out var columnasLlave))
            {
                infracciones.Add($"{tabla}: no tiene llave primaria.");
                continue;
            }

            var esperada = "UUID_" + (tabla.StartsWith("T_", StringComparison.Ordinal) ? tabla[2..] : tabla);
            var nombres = columnasLlave.Select(f => (string)f[2]!).ToList();
            if (nombres.Count != 1 || nombres[0] != esperada)
            {
                infracciones.Add($"{tabla}: la llave primaria es ({string.Join(", ", nombres)}) y debe ser {esperada}.");
            }

            var restriccion = (string)columnasLlave[0][1]!;
            if (restriccion != $"PK_{tabla}")
            {
                infracciones.Add($"{tabla}: la restricción de llave primaria se llama {restriccion} y debe ser PK_{tabla}.");
            }
        }

        // 3. Columnas de auditoría (también en los historiales).
        var columnas = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT t.name, c.name, ty.name, c.scale, c.is_nullable
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0
            """,
            token);
        var columnasPorTabla = columnas.GroupBy(f => (string)f[0]!).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var tabla in tablas.DeNitido.Concat(tablas.Historiales))
        {
            var deLaTabla = columnasPorTabla.GetValueOrDefault(tabla) ?? [];
            foreach (var auditoria in ColumnasDeAuditoria)
            {
                var columna = deLaTabla.FirstOrDefault(f => (string)f[1]! == auditoria);
                if (columna is null)
                {
                    infracciones.Add($"{tabla}: falta la columna de auditoría {auditoria}.");
                    continue;
                }

                var esFecha = auditoria.StartsWith("FECHA_", StringComparison.Ordinal);
                var tipoEsperado = esFecha ? "datetime2" : "uniqueidentifier";
                if ((string)columna[2]! != tipoEsperado)
                {
                    infracciones.Add($"{tabla}.{auditoria}: es {columna[2]} y debe ser {tipoEsperado}.");
                }

                if (esFecha && Convert.ToInt32(columna[3]) != 3)
                {
                    infracciones.Add($"{tabla}.{auditoria}: tiene precisión {columna[3]} y debe ser DATETIME2(3).");
                }

                var debeSerNulo = auditoria.Contains("MODIFICACION", StringComparison.Ordinal);
                if (Convert.ToBoolean(columna[4]) != debeSerNulo)
                {
                    infracciones.Add($"{tabla}.{auditoria}: " + (debeSerNulo ? "debe admitir NULL." : "debe ser NOT NULL."));
                }
            }
        }

        // 4. UUID_EMPRESAS: llave foránea a T_EMPRESAS e índice que empiece por esa columna.
        // T_EMPRESAS no se exige contra sí misma: ahí UUID_EMPRESAS es la llave primaria.
        var llavesForaneas = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT fk.name, tp.name, cp.name, tr.name, cr.name, fk.delete_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
            JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
            JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
            JOIN sys.columns cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
            """,
            token);
        var indices = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT t.name, i.name, i.is_unique, i.is_primary_key, ic.key_ordinal, c.name
            FROM sys.tables t
            JOIN sys.indexes i ON i.object_id = t.object_id AND i.type > 0
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = t.object_id AND c.column_id = ic.column_id
            WHERE t.is_ms_shipped = 0
            ORDER BY t.name, i.name, ic.key_ordinal
            """,
            token);
        var indicesPorTabla = indices
            .GroupBy(f => (string)f[0]!)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(f => (string)f[1]!)
                    .Select(i => new InfoIndice(
                        i.Key,
                        Convert.ToBoolean(i.First()[2]),
                        Convert.ToBoolean(i.First()[3]),
                        i.Select(f => (string)f[5]!).ToList()))
                    .ToList());

        foreach (var tabla in tablas.DeNitido.Where(t => t != "T_EMPRESAS"))
        {
            var tieneColumna = (columnasPorTabla.GetValueOrDefault(tabla) ?? []).Any(f => (string)f[1]! == "UUID_EMPRESAS");
            if (!tieneColumna)
            {
                continue;
            }

            var fkHaciaEmpresas = llavesForaneas.Any(f =>
                (string)f[1]! == tabla && (string)f[2]! == "UUID_EMPRESAS" && (string)f[3]! == "T_EMPRESAS");
            if (!fkHaciaEmpresas)
            {
                infracciones.Add($"{tabla}: UUID_EMPRESAS no tiene llave foránea a T_EMPRESAS.");
            }

            var conIndice = (indicesPorTabla.GetValueOrDefault(tabla) ?? []).Any(i => i.Columnas[0] == "UUID_EMPRESAS");
            if (!conIndice)
            {
                infracciones.Add($"{tabla}: no hay un índice que empiece por UUID_EMPRESAS.");
            }
        }

        // 5. Nombres de llaves foráneas, índices y CHECK; ninguna llave foránea borra en cascada.
        var delNitido = tablas.DeNitido.ToHashSet();
        foreach (var grupo in llavesForaneas.Where(f => delNitido.Contains((string)f[1]!)).GroupBy(f => (string)f[0]!))
        {
            var fila = grupo.First();
            var tabla = (string)fila[1]!;
            var principal = (string)fila[3]!;
            var columna = (string)fila[2]!;
            var nombre = grupo.Key;
            if (nombre != $"FK_{tabla}_{principal}" && nombre != $"FK_{tabla}_{columna}")
            {
                infracciones.Add($"{tabla}: la llave foránea {nombre} debe llamarse FK_{tabla}_{principal} (o FK_{tabla}_{columna}).");
            }

            if (Convert.ToInt32(fila[5]) != 0)
            {
                infracciones.Add($"{tabla}: la llave foránea {nombre} borra o anula en cascada; debe ser ON DELETE NO ACTION.");
            }
        }

        foreach (var tabla in tablas.DeNitido)
        {
            foreach (var indice in (indicesPorTabla.GetValueOrDefault(tabla) ?? []).Where(i => !i.EsLlavePrimaria))
            {
                var prefijo = indice.EsUnico ? "UX" : "IX";
                var esperado = $"{prefijo}_{tabla}_{string.Join('_', indice.Columnas)}";
                if (indice.Nombre != esperado)
                {
                    infracciones.Add($"{tabla}: el índice {indice.Nombre} debe llamarse {esperado}.");
                }
            }
        }

        var restricciones = await ConsultasSql.FilasAsync(
            cadena,
            "SELECT t.name, cc.name FROM sys.check_constraints cc JOIN sys.tables t ON t.object_id = cc.parent_object_id",
            token);
        foreach (var fila in restricciones.Where(f => delNitido.Contains((string)f[0]!)))
        {
            var tabla = (string)fila[0]!;
            var nombre = (string)fila[1]!;
            if (!nombre.StartsWith($"CK_{tabla}_", StringComparison.Ordinal))
            {
                infracciones.Add($"{tabla}: la restricción CHECK {nombre} debe llamarse CK_{tabla}_<COLUMNA>.");
            }
        }

        return infracciones;
    }

    /// <summary>Columnas <c>float</c>, <c>real</c>, <c>money</c> o <c>smallmoney</c> (también por tipo alias) en cualquier tabla.</summary>
    public static async Task<List<string>> ColumnasConTiposProhibidosAsync(string cadena, CancellationToken token)
    {
        var filas = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT t.name, c.name, base.name
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types base ON base.user_type_id = c.system_type_id
            WHERE t.is_ms_shipped = 0 AND c.system_type_id IN (62, 59, 60, 122)
            ORDER BY t.name, c.name
            """,
            token);
        return [.. filas.Select(f => $"{f[0]}.{f[1]}: es {f[2]}; la regla 4 prohíbe float, real y money.")];
    }

    private sealed record InfoIndice(string Nombre, bool EsUnico, bool EsLlavePrimaria, IReadOnlyList<string> Columnas);
}
