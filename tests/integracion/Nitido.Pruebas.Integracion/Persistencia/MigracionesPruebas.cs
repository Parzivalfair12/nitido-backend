using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

public class MigracionesPruebas(ContenedorSqlServer contenedor)
{
    // Migraciones por módulo, en este orden: la que crea las tablas (spec F0-02a, criterio «Tablas»), la que agrega la
    // seguridad por fila (F0-02b, ADR 0003) y, solo en Firmas, las credenciales y el segundo factor (F0-03a) y el
    // ingreso por correo en la política de T_USUARIOS (ADR 0003, «Ingreso»).
    private static readonly (string Modulo, string[] Migraciones)[] MigracionesEsperadas =
    [
        (
            "Firmas",
            [
                "CrearFirmasYUsuarios",
                "AgregarSeguridadPorFilaDeFirmas",
                "AgregarCredencialesAUsuarios",
                "AgregarIngresoPorCorreoEnSeguridadPorFila",
            ]),
        ("Clientes", ["CrearClientes", "AgregarSeguridadPorFilaDeClientes"]),
    ];

    private static readonly string[] TablasEsperadas =
    [
        "T_FIRMAS", "T_USUARIOS", "T_EMPRESAS", "T_USUARIOS_EMPRESAS",
        "T_FIRMAS_HISTORIAL", "T_USUARIOS_HISTORIAL", "T_EMPRESAS_HISTORIAL", "T_USUARIOS_EMPRESAS_HISTORIAL",
        "__EFMigrationsHistory_Firmas", "__EFMigrationsHistory_Clientes",
    ];

    [Fact]
    public async Task Migraciones_SeAplicanSobreBaseLimpia()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);

        var antes = await ConsultasSql.EscalarAsync(cadena, "SELECT COUNT(*) FROM sys.tables", token);
        Assert.Equal(0, Convert.ToInt32(antes));

        // El orden de la lista es el de aplicación: Firmas antes que Clientes (ADR 0002).
        Assert.Equal(
            MigracionesEsperadas.Select(m => m.Modulo),
            AyudanteDeMigraciones.ContextosEnOrden.Select(c => c.Modulo));

        // Cada módulo trae sus migraciones con el nombre del diseño y en el orden del diseño.
        foreach (var (modulo, tipo) in AyudanteDeMigraciones.ContextosEnOrden)
        {
            var esperadas = MigracionesEsperadas.Single(m => m.Modulo == modulo).Migraciones;
            using var alcance = fabrica.Services.CreateScope();
            var contexto = (DbContext)alcance.ServiceProvider.GetRequiredService(tipo);
            var migraciones = contexto.Database.GetMigrations().ToList();
            Assert.True(
                migraciones.Count == esperadas.Length
                && esperadas.Zip(migraciones).All(par => par.Second.EndsWith("_" + par.First, StringComparison.Ordinal)),
                $"El módulo {modulo} debe tener las migraciones [{string.Join(", ", esperadas)}], en ese orden. Encontradas: [{string.Join(", ", migraciones)}].");
        }

        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);

        var tablas = (await ConsultasSql.FilasAsync(cadena, "SELECT name FROM sys.tables", token))
            .Select(f => (string)f[0]!)
            .ToHashSet();
        foreach (var esperada in TablasEsperadas)
        {
            Assert.True(tablas.Contains(esperada), $"Después de migrar no existe la tabla {esperada}. Existen: [{string.Join(", ", tablas.Order())}].");
        }

        // Cada módulo dejó su migración en su propio historial; no existe el historial sin sufijo.
        Assert.DoesNotContain("__EFMigrationsHistory", tablas);
        foreach (var (modulo, migraciones) in MigracionesEsperadas)
        {
            var ids = (await ConsultasSql.FilasAsync(
                cadena,
                $"SELECT MigrationId FROM [__EFMigrationsHistory_{modulo}]",
                token)).Select(f => (string)f[0]!).ToList();
            foreach (var migracion in migraciones)
            {
                Assert.True(
                    ids.Any(i => i.EndsWith("_" + migracion, StringComparison.Ordinal)),
                    $"__EFMigrationsHistory_{modulo} no registra {migracion}. Registra: [{string.Join(", ", ids)}].");
            }
        }

        // La seguridad por fila queda creada y encendida al terminar las migraciones (ADR 0003).
        var politicas = await ConsultasSql.FilasAsync(
            cadena,
            "SELECT SCHEMA_NAME(schema_id), name, is_enabled FROM sys.security_policies ORDER BY name",
            token);
        Assert.Equal(["PS_CLIENTES", "PS_FIRMAS"], politicas.Select(f => (string)f[1]!));
        Assert.All(politicas, f =>
        {
            Assert.Equal("seguridad", f[0]);
            Assert.True(Convert.ToBoolean(f[2]), $"La política {f[1]} quedó apagada al terminar las migraciones.");
        });

        // No queda nada pendiente y aplicar de nuevo no hace daño.
        foreach (var (_, tipo) in AyudanteDeMigraciones.ContextosEnOrden)
        {
            using var alcance = fabrica.Services.CreateScope();
            var contexto = (DbContext)alcance.ServiceProvider.GetRequiredService(tipo);
            Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(token));
        }

        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);
    }

    [Fact]
    public async Task Migraciones_ClientesAntesQueFirmas_Falla()
    {
        // Documenta por qué el orden Firmas -> Clientes importa: Clientes tiene llaves foráneas hacia
        // tablas de Firmas (ADR 0002). Si dejara de fallar, la dependencia entre módulos se perdió.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);

        using var alcance = fabrica.Services.CreateScope();
        var clientes = (DbContext)alcance.ServiceProvider.GetRequiredService(AyudanteDeMigraciones.ContextosEnOrden[1].Contexto);

        Assert.NotEmpty(clientes.Database.GetMigrations());
        await Assert.ThrowsAsync<SqlException>(() => clientes.Database.MigrateAsync(token));
    }

    private static async Task<List<string>> NombresDePoliticasAsync(string cadena, CancellationToken token) =>
        [.. (await ConsultasSql.FilasAsync(cadena, "SELECT name FROM sys.security_policies ORDER BY name", token)).Select(f => (string)f[0]!)];

    /// <summary>Funciones del esquema <c>seguridad</c> (las de predicados), por nombre.</summary>
    private static async Task<List<string>> NombresDeFuncionesDeSeguridadAsync(string cadena, CancellationToken token) =>
    [
        .. (await ConsultasSql.FilasAsync(
            cadena,
            "SELECT o.name FROM sys.objects o WHERE o.schema_id = SCHEMA_ID('seguridad') AND o.type IN ('IF', 'FN', 'TF') ORDER BY o.name",
            token)).Select(f => (string)f[0]!),
    ];

    private static async Task<int> ContarEsquemaDeSeguridadAsync(string cadena, CancellationToken token) =>
        Convert.ToInt32(await ConsultasSql.EscalarAsync(cadena, "SELECT COUNT(*) FROM sys.schemas WHERE name = 'seguridad'", token));

    private static string[] TablasDeNitido(IEnumerable<string> tablas) =>
        [.. tablas.Where(t => t.StartsWith("T_", StringComparison.Ordinal)).Order()];

    private static async Task<List<string>> ListarTablasAsync(string cadena, CancellationToken token) =>
        [.. (await ConsultasSql.FilasAsync(cadena, "SELECT name FROM sys.tables", token)).Select(f => (string)f[0]!)];

    private static async Task BajarAsync(FabricaNitido fabrica, Type tipoDeContexto, CancellationToken token)
    {
        using var alcance = fabrica.Services.CreateScope();
        var contexto = (DbContext)alcance.ServiceProvider.GetRequiredService(tipoDeContexto);
        // "0" es la base de datos sin ninguna migración: ejecuta el Down de todas las del módulo.
        await contexto.GetService<IMigrator>().MigrateAsync("0", token);
    }

    [Fact]
    public async Task Migraciones_Down_EnElOrdenClientesLuegoFirmas_DejaSoloLosHistoriales()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);
        Assert.Equal(8, TablasDeNitido(await ListarTablasAsync(cadena, token)).Length);

        // Orden inverso al de aplicación: primero Clientes (que referencia), después Firmas.
        await BajarAsync(fabrica, typeof(ClientesDbContext), token);
        var trasClientes = await ListarTablasAsync(cadena, token);
        Assert.Equal(
            ["T_FIRMAS", "T_FIRMAS_HISTORIAL", "T_USUARIOS", "T_USUARIOS_HISTORIAL"],
            TablasDeNitido(trasClientes));

        // El Down de Clientes quita su política y sus tres funciones; la de Firmas y el esquema siguen.
        Assert.Equal(["PS_FIRMAS"], await NombresDePoliticasAsync(cadena, token));
        Assert.Equal(["FN_FIRMAS_ES_DE_LA_FIRMA", "FN_USUARIOS_ES_VISIBLE"], await NombresDeFuncionesDeSeguridadAsync(cadena, token));
        Assert.Equal(1, await ContarEsquemaDeSeguridadAsync(cadena, token));

        await BajarAsync(fabrica, typeof(FirmasDbContext), token);
        var restantes = (await ListarTablasAsync(cadena, token)).Order().ToList();
        Assert.Equal(["__EFMigrationsHistory_Clientes", "__EFMigrationsHistory_Firmas"], restantes);

        // Y el Down de Firmas deja la base sin esquema `seguridad`, sin funciones y sin políticas.
        Assert.Empty(await NombresDePoliticasAsync(cadena, token));
        Assert.Empty(await NombresDeFuncionesDeSeguridadAsync(cadena, token));
        Assert.Equal(0, await ContarEsquemaDeSeguridadAsync(cadena, token));
        var checksDeLaFirma = await ConsultasSql.EscalarAsync(
            cadena, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_T_FIRMAS_UUID_FIRMAS'", token);
        Assert.Equal(0, Convert.ToInt32(checksDeLaFirma));

        // Los historiales quedan sin migraciones registradas.
        foreach (var modulo in new[] { "Firmas", "Clientes" })
        {
            var registradas = await ConsultasSql.EscalarAsync(cadena, $"SELECT COUNT(*) FROM [__EFMigrationsHistory_{modulo}]", token);
            Assert.Equal(0, Convert.ToInt32(registradas));
        }

        // Y se puede volver a subir todo.
        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);
        Assert.Equal(8, TablasDeNitido(await ListarTablasAsync(cadena, token)).Length);
    }

    [Fact]
    public async Task Migraciones_Down_DeFirmasConClientesAplicado_FallaYDejaTodoIntacto()
    {
        // Clientes tiene llaves foráneas hacia T_FIRMAS y T_USUARIOS: no se puede bajar Firmas antes que Clientes.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);

        // Primero se bajan las dos migraciones de Firmas que no tocan a Clientes (ingreso por correo y credenciales):
        // esas sí se pueden revertir. La que sigue (la seguridad por fila de Firmas) es la que no puede.
        using (var alcanceDeBajada = fabrica.Services.CreateScope())
        {
            var firmas = alcanceDeBajada.ServiceProvider.GetRequiredService<FirmasDbContext>();
            var destino = firmas.Database.GetMigrations().Single(m => m.EndsWith("_AgregarSeguridadPorFilaDeFirmas", StringComparison.Ordinal));
            await firmas.GetService<IMigrator>().MigrateAsync(destino, token);
        }

        var tablasAntes = (await ListarTablasAsync(cadena, token)).Order().ToList();
        var historialAntes = await ConsultasSql.FilasAsync(cadena, "SELECT MigrationId FROM [__EFMigrationsHistory_Firmas] ORDER BY MigrationId", token);
        Assert.Equal(2, historialAntes.Count); // CrearFirmasYUsuarios y AgregarSeguridadPorFilaDeFirmas
        var politicasAntes = await NombresDePoliticasAsync(cadena, token);
        var funcionesAntes = await NombresDeFuncionesDeSeguridadAsync(cadena, token);
        Assert.Equal(["PS_CLIENTES", "PS_FIRMAS"], politicasAntes);

        // El Down de AgregarSeguridadPorFilaDeFirmas es el primero en correr y falla al quitar el esquema
        // `seguridad`, que todavía tiene los objetos de Clientes; EF lo deshace entero.
        await Assert.ThrowsAsync<SqlException>(() => BajarAsync(fabrica, typeof(FirmasDbContext), token));

        // Todo sigue como estaba: las mismas tablas, las dos migraciones de Firmas registradas, la seguridad por
        // fila intacta y encendida.
        Assert.Equal(tablasAntes, (await ListarTablasAsync(cadena, token)).Order().ToList());
        var historialDespues = await ConsultasSql.FilasAsync(cadena, "SELECT MigrationId FROM [__EFMigrationsHistory_Firmas] ORDER BY MigrationId", token);
        Assert.Equal(historialAntes.Select(f => (string)f[0]!), historialDespues.Select(f => (string)f[0]!));
        Assert.Equal(politicasAntes, await NombresDePoliticasAsync(cadena, token));
        Assert.Equal(funcionesAntes, await NombresDeFuncionesDeSeguridadAsync(cadena, token));
        var apagadas = await ConsultasSql.EscalarAsync(cadena, "SELECT COUNT(*) FROM sys.security_policies WHERE is_enabled = 0", token);
        Assert.Equal(0, Convert.ToInt32(apagadas));
        var checkDeLaFirma = await ConsultasSql.EscalarAsync(
            cadena, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_T_FIRMAS_UUID_FIRMAS'", token);
        Assert.Equal(1, Convert.ToInt32(checkDeLaFirma));
        var llavesForaneas = await ConsultasSql.EscalarAsync(cadena, "SELECT COUNT(*) FROM sys.foreign_keys", token);
        Assert.Equal(4, Convert.ToInt32(llavesForaneas));

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        Assert.Equal(2, (await contexto.Database.GetPendingMigrationsAsync(token)).Count());
    }

    /// <summary>
    /// Una base local de un desarrollador puede tener filas en <c>T_USUARIOS_EMPRESAS</c> (y en su historial) de
    /// antes de F0-02b, y <c>ADD ... NOT NULL</c> fallaría. La migración <c>AgregarSeguridadPorFilaDeClientes</c>
    /// agrega <c>UUID_FIRMAS</c> como nula, la rellena desde <c>T_EMPRESAS</c> en las dos tablas (con el versionado
    /// apagado) y recién entonces la pasa a NOT NULL, antes de crear la política (después de ella, la migración, que
    /// corre sin sesión, no vería ninguna fila). Se prueba con dos firmas para que cada acceso reciba la firma de
    /// <b>su</b> cliente y no una cualquiera.
    /// </summary>
    [Fact]
    public async Task Migraciones_AccesosExistentesRecibenLaFirmaDeSuCliente()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CrearBaseVaciaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);

        // Firmas completo y Clientes hasta CrearClientes: la base «de antes» de F0-02b en el lado de Clientes.
        using (var alcance = fabrica.Services.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<FirmasDbContext>().Database.MigrateAsync(token);
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var destino = clientes.Database.GetMigrations().Single(m => m.EndsWith("_CrearClientes", StringComparison.Ordinal));
            await clientes.GetService<IMigrator>().MigrateAsync(destino, token);
            var pendientes = (await clientes.Database.GetPendingMigrationsAsync(token)).ToList();
            Assert.True(
                pendientes.Count == 1 && pendientes[0].EndsWith("_AgregarSeguridadPorFilaDeClientes", StringComparison.Ordinal),
                $"Antes de la prueba debía quedar pendiente solo AgregarSeguridadPorFilaDeClientes y quedan [{string.Join(", ", pendientes)}].");
        }

        // Siembra con SQL directo, con el esquema anterior (sin UUID_FIRMAS en el acceso): dos firmas, cada una con
        // su usuario, su cliente y su acceso. La política de Firmas ya está activa, así que cada fila va con la
        // sesión de su firma.
        var firmaA = await SembrarAccesoAnteriorAsync(cadena, "A", token);
        var firmaB = await SembrarAccesoAnteriorAsync(cadena, "B", token);

        // Que el acceso cambie, para que su historial tenga filas con el esquema anterior.
        await Task.Delay(50, token);
        foreach (var firma in new[] { firmaA, firmaB })
        {
            var tocados = await ConsultasSql.EjecutarAsync(
                cadena,
                firma.Sesion,
                "UPDATE T_USUARIOS_EMPRESAS SET FECHA_MODIFICACION = SYSUTCDATETIME(), UUID_USUARIOS_MODIFICACION = @usuario WHERE UUID_USUARIOS_EMPRESAS = @acceso",
                token,
                ("@usuario", firma.Sesion.UuidUsuarios),
                ("@acceso", firma.Acceso));
            Assert.Equal(1, tocados);
            var enElHistorial = await ConsultasSql.EscalarAsync(
                cadena,
                firma.Sesion,
                "SELECT COUNT(*) FROM T_USUARIOS_EMPRESAS_HISTORIAL WHERE UUID_USUARIOS_EMPRESAS = @acceso",
                token,
                ("@acceso", firma.Acceso));
            Assert.True(Convert.ToInt32(enElHistorial) >= 1, "La siembra no dejó filas en el historial del acceso; la prueba no probaría el relleno del historial.");
        }

        // Aplica lo que falta: AgregarSeguridadPorFilaDeClientes.
        await AyudanteDeMigraciones.AplicarAsync(fabrica.Services, token);

        foreach (var (firma, otra) in new[] { (firmaA, firmaB), (firmaB, firmaA) })
        {
            // El acceso y su historial tienen la UUID_FIRMAS del cliente (vistos con la sesión de esa firma).
            var accesos = await ConsultasSql.FilasAsync(
                cadena,
                firma.Sesion,
                "SELECT UUID_FIRMAS FROM T_USUARIOS_EMPRESAS WHERE UUID_USUARIOS_EMPRESAS = @acceso",
                token,
                ("@acceso", firma.Acceso));
            Assert.Equal(firma.Sesion.UuidFirmas, (Guid)Assert.Single(accesos)[0]!);

            var historial = await ConsultasSql.FilasAsync(
                cadena,
                firma.Sesion,
                "SELECT UUID_FIRMAS FROM T_USUARIOS_EMPRESAS_HISTORIAL WHERE UUID_USUARIOS_EMPRESAS = @acceso",
                token,
                ("@acceso", firma.Acceso));
            Assert.NotEmpty(historial);
            Assert.All(historial, f => Assert.Equal(firma.Sesion.UuidFirmas, (Guid)f[0]!));

            // La política quedó activa: el cliente propio se ve (por el acceso ya rellenado) y el ajeno no.
            Assert.Equal(1, await ConsultasSql.ContarAsync(cadena, firma.Sesion, "T_EMPRESAS", token));
            var delOtro = await ConsultasSql.EscalarAsync(
                cadena,
                firma.Sesion,
                "SELECT COUNT(*) FROM T_USUARIOS_EMPRESAS WHERE UUID_USUARIOS_EMPRESAS = @acceso",
                token,
                ("@acceso", otra.Acceso));
            Assert.Equal(0, Convert.ToInt32(delOtro));
        }

        // Y la columna quedó NOT NULL, con la política de Clientes encendida.
        var nulable = await ConsultasSql.EscalarAsync(
            cadena,
            "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.T_USUARIOS_EMPRESAS') AND name = 'UUID_FIRMAS'",
            token);
        Assert.False(Convert.ToBoolean(nulable), "T_USUARIOS_EMPRESAS.UUID_FIRMAS debe ser NOT NULL.");
        var encendida = await ConsultasSql.EscalarAsync(
            cadena, "SELECT is_enabled FROM sys.security_policies WHERE name = 'PS_CLIENTES'", token);
        Assert.True(Convert.ToBoolean(encendida), "La política PS_CLIENTES debe quedar encendida al terminar la migración.");
    }

    private sealed record AccesoAnterior(SesionSql Sesion, Guid Cliente, Guid Acceso);

    private static async Task<AccesoAnterior> SembrarAccesoAnteriorAsync(string cadena, string etiqueta, CancellationToken token)
    {
        var firma = Guid.CreateVersion7();
        var usuario = Guid.CreateVersion7();
        var cliente = Guid.CreateVersion7();
        var acceso = Guid.CreateVersion7();
        var sesion = new SesionSql(usuario, firma);
        var correo = DatosDePrueba.CorreoUnico();

        await ConsultasSql.EjecutarAsync(
            cadena,
            sesion,
            """
            INSERT INTO T_FIRMAS (UUID_FIRMAS, NOMBRE, FECHA_CREACION, UUID_USUARIOS_CREACION)
            VALUES (@firma, @nombre, SYSUTCDATETIME(), @usuario);
            INSERT INTO T_USUARIOS (UUID_USUARIOS, UUID_FIRMAS, NOMBRE, CORREO, CORREO_NORMALIZADO, ROL, ACTIVO, FECHA_CREACION, UUID_USUARIOS_CREACION)
            VALUES (@usuario, @firma, N'Usuario anterior', @correo, @correo, 'ADMINISTRADOR', 1, SYSUTCDATETIME(), @usuario);
            INSERT INTO T_EMPRESAS (UUID_EMPRESAS, UUID_FIRMAS, TIPO_DOCUMENTO, NUMERO_DOCUMENTO, DIGITO_VERIFICACION, RAZON_SOCIAL, ACTIVO, FECHA_CREACION, UUID_USUARIOS_CREACION)
            VALUES (@cliente, @firma, '31', @nit, '7', N'Cliente anterior', 1, SYSUTCDATETIME(), @usuario);
            INSERT INTO T_USUARIOS_EMPRESAS (UUID_USUARIOS_EMPRESAS, UUID_USUARIOS, UUID_EMPRESAS, FECHA_CREACION, UUID_USUARIOS_CREACION)
            VALUES (@acceso, @usuario, @cliente, SYSUTCDATETIME(), @usuario);
            """,
            token,
            ("@firma", firma),
            ("@usuario", usuario),
            ("@cliente", cliente),
            ("@acceso", acceso),
            ("@nombre", $"Firma anterior {etiqueta} {Guid.NewGuid():N}"),
            ("@correo", correo),
            ("@nit", DatosDePrueba.NitUnico()));
        return new AccesoAnterior(sesion, cliente, acceso);
    }
}
