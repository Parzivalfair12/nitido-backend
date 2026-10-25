using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Segunda barrera de la multiempresa (ADR 0003): la seguridad por fila de SQL Server (<c>SESSION_CONTEXT</c> más
/// las políticas <c>seguridad.PS_FIRMAS</c> y <c>seguridad.PS_CLIENTES</c>). Todas usan la base compartida migrada,
/// que tiene las políticas <b>encendidas</b> (ninguna prueba las apaga aquí), y dos firmas sembradas con
/// <see cref="SiembraDeDosFirmas"/>. Con <c>sa</c> en local, que es <c>db_owner</c>, cada prueba también demuestra que
/// el predicado no tiene excepciones por rol. Toda prueba negativa lleva su control positivo (la misma consulta con
/// la sesión dueña sí ve la fila) para que no pase en vacío.
/// </summary>
public class SeguridadPorFilaPruebas(ContenedorSqlServer contenedor)
{
    private const int ErrorDeBloqueo = 33504; // «has a block predicate that conflicts with this operation»
    private const int ErrorDeLlaveForanea = 547;
    private const int ErrorDeLlaveDeSoloLectura = 15664; // «The key has been set as read_only for this session»

    // Tablas actuales y sus historiales, con la columna de llave de cada una.
    private static readonly (string Tabla, string Llave)[] Tablas =
    [
        ("T_FIRMAS", "UUID_FIRMAS"),
        ("T_USUARIOS", "UUID_USUARIOS"),
        ("T_EMPRESAS", "UUID_EMPRESAS"),
        ("T_USUARIOS_EMPRESAS", "UUID_USUARIOS_EMPRESAS"),
    ];

    private async Task<(FabricaNitido Fabrica, string Cadena, SiembraDeDosFirmas Siembra)> PrepararAsync(CancellationToken token)
    {
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        var fabrica = new FabricaNitido(cadena);
        var siembra = await SiembraDeDosFirmas.CrearAsync(fabrica, cadena, token);
        return (fabrica, cadena, siembra);
    }

    // ---------------------------------------------------------------------------------------
    // Cobertura (criterio): sys.security_predicates y sys.security_policies
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Falla (devuelve infracciones) si una tabla con <c>UUID_EMPRESAS</c> o <c>UUID_FIRMAS</c> (también las
    /// <c>_HISTORIAL</c>) no tiene un FILTER en una política encendida; si una tabla actual no tiene los cuatro
    /// BLOCK (AFTER INSERT, AFTER UPDATE, BEFORE UPDATE, BEFORE DELETE); si un historial tiene un BLOCK; o si una
    /// política no está ligada al esquema (<c>is_schema_bound</c>).
    /// </summary>
    private static async Task<(List<string> Infracciones, List<string> TablasRevisadas, int Politicas)> InfraccionesDeCoberturaAsync(
        string cadena,
        CancellationToken token)
    {
        var infracciones = new List<string>();

        var tablas = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT SCHEMA_NAME(t.schema_id), t.name, t.object_id, t.temporal_type
            FROM sys.tables t
            WHERE t.is_ms_shipped = 0
              AND t.name NOT LIKE '\_\_EFMigrationsHistory%' ESCAPE '\'
              AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.name IN ('UUID_EMPRESAS', 'UUID_FIRMAS'))
            """,
            token);
        var predicados = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT sp.target_object_id, sp.predicate_type_desc, sp.operation_desc, pol.name, pol.is_enabled, pol.is_schema_bound
            FROM sys.security_predicates sp
            JOIN sys.security_policies pol ON pol.object_id = sp.object_id
            """,
            token);
        var politicas = await ConsultasSql.FilasAsync(
            cadena, "SELECT name, is_enabled, is_schema_bound FROM sys.security_policies", token);

        if (politicas.Count == 0)
        {
            infracciones.Add("No hay ninguna política de seguridad (sys.security_policies está vacía).");
        }

        foreach (var politica in politicas)
        {
            if (!Convert.ToBoolean(politica[1]))
            {
                infracciones.Add($"La política {politica[0]} no está activa (is_enabled = 0).");
            }

            if (!Convert.ToBoolean(politica[2]))
            {
                infracciones.Add($"La política {politica[0]} no está ligada al esquema (is_schema_bound = 0).");
            }
        }

        foreach (var tabla in tablas)
        {
            var nombre = (string)tabla[1]!;
            var objeto = Convert.ToInt32(tabla[2]);
            var esHistorial = Convert.ToInt32(tabla[3]) == 1;
            var deLaTabla = predicados.Where(p => Convert.ToInt32(p[0]) == objeto).ToList();

            if (!deLaTabla.Any(p => (string)p[1]! == "FILTER" && Convert.ToBoolean(p[4])))
            {
                infracciones.Add($"{nombre}: tiene UUID_EMPRESAS o UUID_FIRMAS y no tiene un predicado FILTER en una política activa.");
            }

            var operaciones = deLaTabla.Where(p => (string)p[1]! == "BLOCK").Select(p => (string)p[2]!).ToHashSet();
            if (esHistorial)
            {
                if (operaciones.Count > 0)
                {
                    infracciones.Add($"{nombre}: el historial no debe tener predicados BLOCK (frenarían el versionado del sistema) y tiene [{string.Join(", ", operaciones)}].");
                }
            }
            else
            {
                foreach (var operacion in new[] { "AFTER INSERT", "AFTER UPDATE", "BEFORE UPDATE", "BEFORE DELETE" })
                {
                    if (!operaciones.Contains(operacion))
                    {
                        infracciones.Add($"{nombre}: falta el predicado BLOCK {operacion}.");
                    }
                }
            }
        }

        return (infracciones, [.. tablas.Select(t => (string)t[1]!)], politicas.Count);
    }

    [Fact]
    public async Task SeguridadPorFila_CubreTodaTablaDeEmpresaYFirma()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);

        var (infracciones, tablasRevisadas, politicas) = await InfraccionesDeCoberturaAsync(cadena, token);

        // Que no pase en vacío: las cuatro tablas y sus cuatro historiales, bajo las dos políticas (en la fase 1
        // llegarán más tablas con UUID_EMPRESAS, y la regla las revisará también).
        string[] esperadas =
        [
            "T_FIRMAS", "T_USUARIOS", "T_EMPRESAS", "T_USUARIOS_EMPRESAS",
            "T_FIRMAS_HISTORIAL", "T_USUARIOS_HISTORIAL", "T_EMPRESAS_HISTORIAL", "T_USUARIOS_EMPRESAS_HISTORIAL",
        ];
        Assert.All(esperadas, tabla => Assert.Contains(tabla, tablasRevisadas));
        Assert.True(politicas >= 2, $"Se esperaban al menos las políticas PS_FIRMAS y PS_CLIENTES y hay {politicas}.");
        Assert.True(infracciones.Count == 0, "La seguridad por fila no cubre todo:\n- " + string.Join("\n- ", infracciones));
    }

    [Fact]
    public async Task SeguridadPorFila_LaPruebaDeCoberturaDetectaUnaPoliticaApagada()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseMigradaPropiaAsync(token);
        await ConsultasSql.EjecutarAsync(cadena, "ALTER SECURITY POLICY seguridad.PS_CLIENTES WITH (STATE = OFF)", token);

        var (infracciones, _, _) = await InfraccionesDeCoberturaAsync(cadena, token);

        Assert.Contains(infracciones, i => i.Contains("PS_CLIENTES", StringComparison.Ordinal) && i.Contains("no está activa", StringComparison.Ordinal));
        Assert.Contains(infracciones, i => i.StartsWith("T_EMPRESAS:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeguridadPorFila_LaPruebaDeCoberturaDetectaUnaTablaSinPredicado()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await BaseMigradaPropiaAsync(token);

        // 1. Una tabla nueva con UUID_FIRMAS y sin ningún predicado.
        await ConsultasSql.EjecutarAsync(cadena, "CREATE TABLE dbo.T_TABLA_NUEVA (UUID_TABLA_NUEVA UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, UUID_FIRMAS UNIQUEIDENTIFIER NOT NULL)", token);
        // 2. Una tabla que pierde su FILTER y un BLOCK.
        await ConsultasSql.EjecutarAsync(
            cadena,
            """
            ALTER SECURITY POLICY seguridad.PS_FIRMAS DROP FILTER PREDICATE ON dbo.T_USUARIOS;
            ALTER SECURITY POLICY seguridad.PS_FIRMAS DROP BLOCK PREDICATE ON dbo.T_FIRMAS BEFORE DELETE;
            """,
            token);

        var (infracciones, _, _) = await InfraccionesDeCoberturaAsync(cadena, token);

        Assert.Contains(infracciones, i => i.StartsWith("T_TABLA_NUEVA:", StringComparison.Ordinal) && i.Contains("FILTER", StringComparison.Ordinal));
        Assert.Contains(infracciones, i => i.StartsWith("T_USUARIOS:", StringComparison.Ordinal) && i.Contains("FILTER", StringComparison.Ordinal));
        Assert.Contains(infracciones, i => i.StartsWith("T_FIRMAS:", StringComparison.Ordinal) && i.Contains("BEFORE DELETE", StringComparison.Ordinal));
        Assert.DoesNotContain(infracciones, i => i.StartsWith("T_EMPRESAS:", StringComparison.Ordinal));
    }

    private async Task<string> BaseMigradaPropiaAsync(CancellationToken token)
    {
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);
        return cadena;
    }

    [Fact]
    public async Task SeguridadPorFila_LasFuncionesSonEnLineaConSchemabindingYSinExcepcionesPorRol()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);

        var funciones = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT o.name, o.type, OBJECTPROPERTY(o.object_id, 'IsSchemaBound'), m.definition
            FROM sys.objects o
            JOIN sys.sql_modules m ON m.object_id = o.object_id
            WHERE o.schema_id = SCHEMA_ID('seguridad')
            """,
            token);

        // Las cuatro de la fase 0 existen (las funciones de otros módulos de la fase 1 también se revisan abajo).
        string[] esperadas = ["FN_CLIENTES_ES_ACCESO_DEL_USUARIO", "FN_CLIENTES_ES_CLIENTE_PERMITIDO", "FN_CLIENTES_ES_DE_LA_FIRMA", "FN_FIRMAS_ES_DE_LA_FIRMA"];
        Assert.All(esperadas, nombre => Assert.Contains(nombre, funciones.Select(f => (string)f[0]!)));
        string[] excepcionesPorRol = ["IS_MEMBER", "IS_SRVROLEMEMBER", "IS_ROLEMEMBER", "USER_NAME", "SUSER_", "SYSTEM_USER", "SESSION_USER", "CURRENT_USER", "ORIGINAL_LOGIN", "HAS_PERMS_BY_NAME"];
        foreach (var funcion in funciones)
        {
            var nombre = (string)funcion[0]!;
            Assert.Equal("IF", ((string)funcion[1]!).Trim()); // función de tabla en línea
            Assert.Equal(1, Convert.ToInt32(funcion[2])); // SCHEMABINDING
            var definicion = (string)funcion[3]!;
            Assert.Contains("SESSION_CONTEXT", definicion, StringComparison.Ordinal);
            foreach (var prohibida in excepcionesPorRol)
            {
                Assert.False(
                    definicion.Contains(prohibida, StringComparison.OrdinalIgnoreCase),
                    $"{nombre} usa {prohibida}: el predicado no puede tener excepciones por rol ni por usuario de la base (con sa, la barrera quedaría apagada).");
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Aislamiento (criterios)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SeguridadPorFila_SinFiltroDeEfNoDevuelveFilasAjenas()
    {
        var token = TestContext.Current.CancellationToken;
        var (fabrica, cadena, s) = await PrepararAsync(token);
        await using var anfitrion = fabrica;
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);

        // 1. EF con IgnoreQueryFilters(): solo queda la segunda barrera. Como A1 (que solo tiene acceso a CA1).
        using (var alcance = fabrica.Services.CreateScope())
        {
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var firmas = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();

            Assert.Equal([s.CA1], await clientes.Clientes.IgnoreQueryFilters().Select(c => c.Uuid).ToListAsync(token));
            Assert.Equal([s.AccesoA1], await clientes.Accesos.IgnoreQueryFilters().Select(a => a.Uuid).ToListAsync(token));
            Assert.Equal(new[] { s.A1, s.A2 }.Order(), (await firmas.Set<Usuario>().IgnoreQueryFilters().Select(u => u.Uuid).ToListAsync(token)).Order());
            Assert.Equal([s.FirmaA], await firmas.Firmas.IgnoreQueryFilters().Select(f => f.Uuid).ToListAsync(token));
        }

        // 2. SQL directo (Database.SqlQuery), sin ningún filtro de EF: la conexión que abrió EF lleva el contexto.
        using (var alcance = fabrica.Services.CreateScope())
        {
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            foreach (var (tabla, llave) in Tablas)
            {
                // Tabla y llave salen de la lista constante de arriba, no de una entrada del usuario.
#pragma warning disable EF1002
                var vistas = await clientes.Database
                    .SqlQueryRaw<Guid>($"SELECT {llave} AS [Value] FROM {tabla}")
                    .ToListAsync(token);
#pragma warning restore EF1002
                Assert.True(vistas.Count > 0, $"{tabla}: A1 no ve ni sus propias filas por SQL directo (control).");
                Assert.DoesNotContain(s.CB1, vistas);
                Assert.DoesNotContain(s.AccesoB1, vistas);
                Assert.DoesNotContain(s.B1, vistas);
                Assert.DoesNotContain(s.FirmaB, vistas);
            }
        }

        // 3. SqlClient puro con las llaves de A1, con y sin historial y con FOR SYSTEM_TIME ALL.
        var esperadas = new (string Sql, Guid[] Propias)[]
        {
            ("SELECT UUID_EMPRESAS FROM T_EMPRESAS", [s.CA1]),
            ("SELECT UUID_EMPRESAS FROM T_EMPRESAS FOR SYSTEM_TIME ALL", [s.CA1]),
            ("SELECT UUID_EMPRESAS FROM T_EMPRESAS_HISTORIAL", [s.CA1]),
            ("SELECT UUID_USUARIOS_EMPRESAS FROM T_USUARIOS_EMPRESAS", [s.AccesoA1]),
            ("SELECT UUID_USUARIOS_EMPRESAS FROM T_USUARIOS_EMPRESAS_HISTORIAL", [s.AccesoA1]),
            ("SELECT UUID_USUARIOS FROM T_USUARIOS", [s.A1, s.A2]),
            ("SELECT UUID_USUARIOS FROM T_USUARIOS FOR SYSTEM_TIME ALL", [s.A1, s.A2]),
            ("SELECT UUID_USUARIOS FROM T_USUARIOS_HISTORIAL", [s.A1, s.A2]),
            ("SELECT UUID_FIRMAS FROM T_FIRMAS", [s.FirmaA]),
            ("SELECT UUID_FIRMAS FROM T_FIRMAS_HISTORIAL", [s.FirmaA]),
        };
        foreach (var (sql, propias) in esperadas)
        {
            var vistas = await ConsultasSql.LlavesAsync(cadena, s.SesionA1, sql, token);
            Assert.True(propias.Order().SequenceEqual(vistas), $"Como A1, «{sql}» devolvió [{string.Join(", ", vistas)}] y se esperaba solo [{string.Join(", ", propias.Order())}].");
        }

        // Control del lado contrario: B1 ve lo suyo con las mismas consultas, y nada de A.
        Assert.Equal([s.CB1], await ConsultasSql.LlavesAsync(cadena, s.SesionB1, "SELECT UUID_EMPRESAS FROM T_EMPRESAS FOR SYSTEM_TIME ALL", token));
        Assert.Equal([s.B1], await ConsultasSql.LlavesAsync(cadena, s.SesionB1, "SELECT UUID_USUARIOS FROM T_USUARIOS FOR SYSTEM_TIME ALL", token));
    }

    [Fact]
    public async Task SeguridadPorFila_BloqueaEscribirEnOtraFirma()
    {
        var token = TestContext.Current.CancellationToken;
        var (fabrica, cadena, s) = await PrepararAsync(token);
        await using var anfitrion = fabrica;

        // Control positivo: A1 sí puede escribir en su propia firma con las mismas órdenes.
        Assert.Equal(1, await InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Empresa(s.FirmaA, s.A1), token));
        Assert.Equal(1, await InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Usuario(s.FirmaA, s.A1), token));
        Assert.Equal(1, await InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Acceso(s.FirmaA, await ClienteNuevoDeA1Async(cadena, s, token), s.A1, s.A1), token));

        // Como A1: INSERT con la firma de B falla con el error de predicado de bloqueo, en las tres tablas.
        var empresaAjena = await Assert.ThrowsAnyAsync<SqlException>(() =>
            InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Empresa(s.FirmaB, s.A1), token));
        Assert.Equal(ErrorDeBloqueo, empresaAjena.Number);

        var usuarioAjeno = await Assert.ThrowsAnyAsync<SqlException>(() =>
            InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Usuario(s.FirmaB, s.A1), token));
        Assert.Equal(ErrorDeBloqueo, usuarioAjeno.Number);

        var accesoAjeno = await Assert.ThrowsAnyAsync<SqlException>(() =>
            InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Acceso(s.FirmaB, s.CB1, s.A1, s.A1), token));
        Assert.Equal(ErrorDeBloqueo, accesoAjeno.Number);

        // Y un acceso de otro usuario de la misma firma tampoco (el BLOCK pide la firma y el usuario de la sesión).
        var accesoDeOtroUsuario = await Assert.ThrowsAnyAsync<SqlException>(() =>
            InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Acceso(s.FirmaA, s.CA1, s.A2, s.A1), token));
        Assert.Equal(ErrorDeBloqueo, accesoDeOtroUsuario.Number);

        // La firma de B tampoco se puede crear desde la sesión de A.
        var firmaAjena = await Assert.ThrowsAnyAsync<SqlException>(() => ConsultasSql.EjecutarAsync(
            cadena,
            s.SesionA1,
            "INSERT INTO T_FIRMAS (UUID_FIRMAS, NOMBRE, FECHA_CREACION, UUID_USUARIOS_CREACION) VALUES (@llave, N'Firma ajena', SYSUTCDATETIME(), @creador)",
            token,
            ("@llave", Guid.CreateVersion7()),
            ("@creador", s.A1)));
        Assert.Equal(ErrorDeBloqueo, firmaAjena.Number);

        // Por EF: Cliente.Crear con la firma de B falla igual, y nada queda guardado.
        var ajeno = Cliente.Crear(s.FirmaB, "31", DatosDePrueba.NitUnico(), "7", "Cliente en la firma ajena", s.A1, fabrica.Reloj.GetUtcNow());
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            contexto.Add(ajeno);
            var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(() => contexto.SaveChangesAsync(token));
            Assert.Equal(ErrorDeBloqueo, Assert.IsType<SqlException>(ex.GetBaseException()).Number);
        }

        Assert.Equal(0, Convert.ToInt32(await ConsultasSql.EscalarAsync(
            cadena, s.SesionB1, "SELECT COUNT(*) FROM T_EMPRESAS WHERE UUID_EMPRESAS = @llave", token, ("@llave", ajeno.Uuid))));

        // UPDATE que pasa una fila de A a la firma de B: lo frena el BLOCK AFTER UPDATE (sobre T_USUARIOS, donde ninguna
        // llave foránea sale primero), y la fila sigue en A.
        var pasarAB = await Assert.ThrowsAnyAsync<SqlException>(() => ConsultasSql.EjecutarAsync(
            cadena,
            s.SesionA1,
            "UPDATE T_USUARIOS SET UUID_FIRMAS = @firmaB WHERE UUID_USUARIOS = @usuario",
            token,
            ("@firmaB", s.FirmaB),
            ("@usuario", s.A2)));
        Assert.Equal(ErrorDeBloqueo, pasarAB.Number);
        Assert.Equal(s.FirmaA, (Guid)(await ConsultasSql.EscalarAsync(
            cadena, s.SesionA1, "SELECT UUID_FIRMAS FROM T_USUARIOS WHERE UUID_USUARIOS = @usuario", token, ("@usuario", s.A2)))!);

        // Lo mismo sobre un cliente de A: falla (por el BLOCK o por la llave foránea compuesta, según cuál se evalúe
        // primero) y el cliente sigue en A.
        var clienteAB = await Assert.ThrowsAnyAsync<SqlException>(() => ConsultasSql.EjecutarAsync(
            cadena,
            s.SesionA1,
            "UPDATE T_EMPRESAS SET UUID_FIRMAS = @firmaB WHERE UUID_EMPRESAS = @cliente",
            token,
            ("@firmaB", s.FirmaB),
            ("@cliente", s.CA1)));
        Assert.True(clienteAB.Number is ErrorDeBloqueo or ErrorDeLlaveForanea, $"Se esperaba el bloqueo o la llave foránea y fue el error {clienteAB.Number}.");
        Assert.Equal(1, Convert.ToInt32(await ConsultasSql.EscalarAsync(
            cadena, s.SesionA1, "SELECT COUNT(*) FROM T_EMPRESAS WHERE UUID_EMPRESAS = @cliente AND UUID_FIRMAS = @firmaA", token, ("@cliente", s.CA1), ("@firmaA", s.FirmaA))));

        // UPDATE y DELETE sobre una fila de B: el FILTER la oculta, así que afectan 0 filas y B ve su fila intacta.
        var actualizadas = await ConsultasSql.EjecutarAsync(
            cadena,
            s.SesionA1,
            "UPDATE T_EMPRESAS SET RAZON_SOCIAL = N'Cambiada por A' WHERE UUID_EMPRESAS = @cliente",
            token,
            ("@cliente", s.CB1));
        Assert.Equal(0, actualizadas);
        var borradas = await ConsultasSql.EjecutarAsync(
            cadena, s.SesionA1, "DELETE FROM T_EMPRESAS WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", s.CB1));
        Assert.Equal(0, borradas);
        var razonDeB = await ConsultasSql.EscalarAsync(
            cadena, s.SesionB1, "SELECT RAZON_SOCIAL FROM T_EMPRESAS WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", s.CB1));
        Assert.NotEqual("Cambiada por A", razonDeB);
        Assert.NotNull(razonDeB);

        // Control positivo del UPDATE: sobre un cliente propio, con acceso, sí cambia.
        var propias = await ConsultasSql.EjecutarAsync(
            cadena,
            s.SesionA1,
            "UPDATE T_EMPRESAS SET RAZON_SOCIAL = N'Cambiada por su dueño' WHERE UUID_EMPRESAS = @cliente",
            token,
            ("@cliente", s.CA1));
        Assert.Equal(1, propias);

        // Un acceso de A1, con la firma de A, a un cliente de B: el BLOCK pasa, pero la llave foránea compuesta
        // (UUID_EMPRESAS, UUID_FIRMAS) no existe en T_EMPRESAS porque CB1 es de la firma B.
        var accesoACruzado = await Assert.ThrowsAnyAsync<SqlException>(() =>
            InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Acceso(s.FirmaA, s.CB1, s.A1, s.A1), token));
        Assert.Equal(ErrorDeLlaveForanea, accesoACruzado.Number);
        Assert.Contains("FK_T_USUARIOS_EMPRESAS_T_EMPRESAS", accesoACruzado.Message, StringComparison.Ordinal);
    }

    /// <summary>Un cliente propio de A1 para colgarle un acceso en el control positivo (el de la siembra ya tiene el suyo).</summary>
    private static async Task<Guid> ClienteNuevoDeA1Async(string cadena, SiembraDeDosFirmas s, CancellationToken token)
    {
        var cliente = Guid.CreateVersion7();
        var insertadas = await InsertsSql.EjecutarAsync(cadena, s.SesionA1, InsertsSql.Empresa(s.FirmaA, s.A1, cliente), token);
        Assert.Equal(1, insertadas);
        return cliente;
    }

    [Fact]
    public async Task SeguridadPorFila_SinContextoNoDevuelveNada()
    {
        var token = TestContext.Current.CancellationToken;
        var (fabrica, cadena, s) = await PrepararAsync(token);
        await using var anfitrion = fabrica;

        var consultas = Tablas
            .SelectMany(t => new[] { $"SELECT {t.Llave} FROM {t.Tabla}", $"SELECT {t.Llave} FROM {t.Tabla}_HISTORIAL" })
            .Concat(["SELECT UUID_EMPRESAS FROM T_EMPRESAS FOR SYSTEM_TIME ALL", "SELECT UUID_USUARIOS FROM T_USUARIOS FOR SYSTEM_TIME ALL"])
            .ToList();

        // Control: las ocho tablas (cuatro y sus historiales) sí tienen filas de A y de B, vistas con su sesión.
        foreach (var consulta in consultas)
        {
            Assert.NotEmpty(await ConsultasSql.LlavesAsync(cadena, s.SesionA1, consulta, token));
            Assert.NotEmpty(await ConsultasSql.LlavesAsync(cadena, s.SesionB1, consulta, token));
        }

        // 1. SqlConnection directa, sin ninguna llave en SESSION_CONTEXT.
        foreach (var consulta in consultas)
        {
            var vistas = await ConsultasSql.FilasAsync(cadena, consulta, token);
            Assert.True(vistas.Count == 0, $"Sin contexto, «{consulta}» devolvió {vistas.Count} filas.");
        }

        // 2. EF sin sesión (las tres llaves en Guid.Empty), incluso sin el filtro de EF y con SQL directo.
        fabrica.Sesion.Actuar(SesionSql.SinSesion);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
        foreach (var consulta in consultas)
        {
            var vistas = await contexto.Database
                .SqlQueryRaw<Guid>(consulta.Replace(" FROM ", " AS [Value] FROM ", StringComparison.Ordinal))
                .ToListAsync(token);
            Assert.True(vistas.Count == 0, $"Con EF sin sesión, «{consulta}» devolvió {vistas.Count} filas.");
        }

        Assert.Empty(await contexto.Clientes.IgnoreQueryFilters().ToListAsync(token));
        Assert.Empty(await contexto.Accesos.IgnoreQueryFilters().ToListAsync(token));
    }

    [Fact]
    public async Task SeguridadPorFila_UsuarioSinPermisoNoVeElCliente()
    {
        var token = TestContext.Current.CancellationToken;
        var (fabrica, cadena, s) = await PrepararAsync(token);
        await using var anfitrion = fabrica;

        // A1 y A2 son de la misma firma; CA2 solo tiene el acceso de A2.
        Assert.Equal(s.FirmaA, s.SesionA2.UuidFirmas);

        // Como A1: CA2 no aparece ni con IgnoreQueryFilters() ni con SQL directo ni en el historial; CA1 sí.
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var porEf = await clientes.Clientes.IgnoreQueryFilters().Select(c => c.Uuid).ToListAsync(token);
            Assert.Contains(s.CA1, porEf);
            Assert.DoesNotContain(s.CA2, porEf);
        }

        foreach (var sql in new[]
        {
            "SELECT UUID_EMPRESAS FROM T_EMPRESAS",
            "SELECT UUID_EMPRESAS FROM T_EMPRESAS FOR SYSTEM_TIME ALL",
            "SELECT UUID_EMPRESAS FROM T_EMPRESAS_HISTORIAL",
        })
        {
            var vistas = await ConsultasSql.LlavesAsync(cadena, s.SesionA1, sql, token);
            Assert.Contains(s.CA1, vistas);
            Assert.DoesNotContain(s.CA2, vistas);
        }

        // El acceso de A2 tampoco lo ve A1, ni en el historial.
        foreach (var sql in new[] { "SELECT UUID_USUARIOS_EMPRESAS FROM T_USUARIOS_EMPRESAS", "SELECT UUID_USUARIOS_EMPRESAS FROM T_USUARIOS_EMPRESAS_HISTORIAL" })
        {
            var vistas = await ConsultasSql.LlavesAsync(cadena, s.SesionA1, sql, token);
            Assert.Contains(s.AccesoA1, vistas);
            Assert.DoesNotContain(s.AccesoA2, vistas);
        }

        // Sin permiso tampoco puede cambiarlo ni borrarlo: el FILTER lo oculta (0 filas afectadas) y A2, que sí
        // tiene acceso, lo ve intacto.
        var razonAntes = await ConsultasSql.EscalarAsync(
            cadena, s.SesionA2, "SELECT RAZON_SOCIAL FROM T_EMPRESAS WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", s.CA2));
        Assert.NotNull(razonAntes);
        Assert.Equal(0, await ConsultasSql.EjecutarAsync(
            cadena, s.SesionA1, "UPDATE T_EMPRESAS SET RAZON_SOCIAL = N'Cambiada sin permiso' WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", s.CA2)));
        Assert.Equal(0, await ConsultasSql.EjecutarAsync(
            cadena, s.SesionA1, "DELETE FROM T_EMPRESAS WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", s.CA2)));
        var razonDespues = await ConsultasSql.EscalarAsync(
            cadena, s.SesionA2, "SELECT RAZON_SOCIAL FROM T_EMPRESAS WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", s.CA2));
        Assert.Equal(razonAntes, razonDespues);

        // Control: como A2 se ve CA2 y no CA1.
        var deA2 = await ConsultasSql.LlavesAsync(cadena, s.SesionA2, "SELECT UUID_EMPRESAS FROM T_EMPRESAS", token);
        Assert.Equal([s.CA2], deA2);
    }

    [Fact]
    public async Task SeguridadPorFila_PermiteCrearClienteConSuAccesoEnUnGuardado()
    {
        // Riesgo 8.3 del diseño: EF inserta el cliente antes que su primer acceso, en el mismo SaveChanges, y lee
        // con OUTPUT los periodos de las tablas temporales. Ni el orden de los INSERT ni esa lectura pueden chocar
        // con los predicados: el AFTER INSERT de T_EMPRESAS pide solo la firma, no un acceso que todavía no existe.
        var token = TestContext.Current.CancellationToken;
        var (fabrica, cadena, s) = await PrepararAsync(token);
        await using var anfitrion = fabrica;
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);

        var nuevo = Cliente.Crear(s.FirmaA, "31", DatosDePrueba.NitUnico(), "7", "Cliente nuevo de A1", s.A1, fabrica.Reloj.GetUtcNow());
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            contexto.Add(nuevo);
            Assert.Equal(2, await contexto.SaveChangesAsync(token)); // el cliente y su acceso, en un solo guardado
        }

        // A1 lo ve después, con su acceso, por EF y por SQL.
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var leido = await contexto.Clientes.Include(c => c.Accesos).SingleAsync(c => c.Uuid == nuevo.Uuid, token);
            var acceso = Assert.Single(leido.Accesos);
            Assert.Equal(s.A1, acceso.UuidUsuarios);
            Assert.Equal(s.FirmaA, acceso.UuidFirmas);
        }

        Assert.Contains(nuevo.Uuid, await ConsultasSql.LlavesAsync(cadena, s.SesionA1, "SELECT UUID_EMPRESAS FROM T_EMPRESAS", token));

        // Quien no tiene acceso (A2, misma firma) no lo ve: crear un cliente no se lo muestra a toda la firma.
        Assert.DoesNotContain(nuevo.Uuid, await ConsultasSql.LlavesAsync(cadena, s.SesionA2, "SELECT UUID_EMPRESAS FROM T_EMPRESAS", token));
    }

    // ---------------------------------------------------------------------------------------
    // SESSION_CONTEXT: lo que fija el interceptor de conexión
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SesionSql_LlevaLasTresLlavesDeLaSesion()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var usuario = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var empresa = Guid.CreateVersion7();

        // Sin cliente activo: UUID_EMPRESAS queda en Guid.Empty (no sin fijar: decisión 3 del ADR 0003).
        fabrica.Sesion.Actuar(usuario, firma);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var fila = await LecturaDeSesionSql.LeerAsync(alcance.ServiceProvider.GetRequiredService<ClientesDbContext>(), token);
            Assert.Equal(usuario, fila.UuidUsuarios);
            Assert.Equal(firma, fila.UuidFirmas);
            Assert.Equal(Guid.Empty, fila.UuidEmpresas);
            Assert.Equal("uniqueidentifier", fila.TipoDeUuidFirmas); // parámetros tipados, no texto
        }

        // Con cliente activo, por el camino asíncrono y por el síncrono (ConnectionOpenedAsync y ConnectionOpened).
        fabrica.Sesion.Actuar(usuario, firma, empresa);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
            var asincrona = await LecturaDeSesionSql.LeerAsync(contexto, token);
            Assert.Equal(usuario, asincrona.UuidUsuarios);
            Assert.Equal(firma, asincrona.UuidFirmas);
            Assert.Equal(empresa, asincrona.UuidEmpresas);
        }

        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
            var sincrona = LecturaDeSesionSql.Leer(contexto);
            Assert.Equal(usuario, sincrona.UuidUsuarios);
            Assert.Equal(firma, sincrona.UuidFirmas);
            Assert.Equal(empresa, sincrona.UuidEmpresas);
        }
    }

    [Fact]
    public async Task SesionSql_SinSesionFijaLasTresLlavesEnGuidEmpty()
    {
        // Una llave sin fijar se podría fijar después, en la misma conexión, con la firma de otro. Sin sesión se
        // fijan las tres en Guid.Empty y de solo lectura.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        await using var fabrica = new FabricaNitido(cadena); // la sesión de prueba arranca vacía
        await using var sinSesion = new FabricaNitido(cadena, usarSesionDePrueba: false); // ContextoSesionHttp sin HttpContext

        foreach (var anfitrion in new[] { fabrica, sinSesion })
        {
            using var alcance = anfitrion.Services.CreateScope();
            var fila = await LecturaDeSesionSql.LeerAsync(alcance.ServiceProvider.GetRequiredService<ClientesDbContext>(), token);

            Assert.Equal(Guid.Empty, fila.UuidUsuarios);
            Assert.Equal(Guid.Empty, fila.UuidFirmas);
            Assert.Equal(Guid.Empty, fila.UuidEmpresas);
        }
    }

    [Theory]
    [InlineData("UUID_USUARIOS")]
    [InlineData("UUID_FIRMAS")]
    [InlineData("UUID_EMPRESAS")]
    public async Task SesionSql_LlavesSonDeSoloLectura(string llave)
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var usuario = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var otraFirma = Guid.CreateVersion7();

        // Con y sin sesión.
        foreach (var (sesion, descripcion) in new[] { (new SesionSql(usuario, firma, Guid.CreateVersion7()), "con sesión"), (SesionSql.SinSesion, "sin sesión") })
        {
            fabrica.Sesion.Actuar(sesion);
            using var alcance = fabrica.Services.CreateScope();
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();

            // La misma conexión abierta de principio a fin, para que el intento y la lectura vean la misma sesión.
            await contexto.Database.OpenConnectionAsync(token);
            var antes = await LecturaDeSesionSql.LeerAsync(contexto, token);

            var intento = await Assert.ThrowsAnyAsync<Exception>(() => contexto.Database.ExecuteSqlRawAsync(
                "EXEC sys.sp_set_session_context @key = {0}, @value = {1}",
                [llave, otraFirma],
                token));
            Assert.Equal(ErrorDeLlaveDeSoloLectura, Assert.IsType<SqlException>(intento.GetBaseException()).Number);

            var despues = await LecturaDeSesionSql.LeerAsync(contexto, token);
            Assert.Equal(antes.Spid, despues.Spid); // era la misma conexión
            Assert.Equal(antes.UuidUsuarios, despues.UuidUsuarios);
            Assert.Equal(antes.UuidFirmas, despues.UuidFirmas);
            Assert.Equal(antes.UuidEmpresas, despues.UuidEmpresas);
            Assert.True(
                despues.UuidFirmas != otraFirma && despues.UuidUsuarios != otraFirma && despues.UuidEmpresas != otraFirma,
                $"{descripcion}: la llave {llave} cambió a pesar de ser de solo lectura.");
            await contexto.Database.CloseConnectionAsync();
        }
    }
}
