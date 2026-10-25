using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Auditoría de las cuatro tablas. Desde F0-02b todo se guarda con la sesión de quien lo crea y toda lectura de
/// verificación con SQL directo fija esa misma sesión: sin ella la seguridad por fila no devuelve ninguna fila y
/// una aserción como «no se guardó» pasaría siempre. Por eso las aserciones negativas llevan su control positivo.
/// </summary>
public class AuditoriaPruebas(ContenedorSqlServer contenedor)
{
    // Milisegundos exactos: así el redondeo de DATETIME2(3) no produce falsos fallos.
    private static readonly DateTime Creacion = new(2031, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc);
    private static readonly DateTime Modificacion = new(2031, 3, 5, 9, 10, 11, 456, DateTimeKind.Utc);

    private static Task<AuditoriaDeFila> LeerAsync(string cadena, SesionSql sesion, string tabla, string columnaLlave, Guid llave, CancellationToken token) =>
        AuditoriaDeFila.LeerAsync(cadena, sesion, tabla, columnaLlave, llave, token);

    [Fact]
    public async Task Auditoria_LlenaFechasYUsuario()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        fabrica.Reloj.Ahora = new DateTimeOffset(Creacion);

        // Al crear: las cuatro tablas, con la hora del reloj y el usuario de la sesión. El usuario que crea la
        // firma, el usuario, el cliente y su acceso es el mismo: la sesión que los BLOCK dejan pasar a las cuatro.
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var creador = usuario.Uuid;
        var sesionDelCreador = new SesionSql(creador, firma.Uuid);
        var cliente = DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid);
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);
        var (segundoUsuario, _) = await DatosDePrueba.DarAccesoAUnUsuarioNuevoAsync(fabrica, firma.Uuid, cliente.Uuid, creador, token);

        var creadas = new (string Tabla, string Llave, Guid Uuid)[]
        {
            ("T_FIRMAS", "UUID_FIRMAS", firma.Uuid),
            ("T_USUARIOS", "UUID_USUARIOS", usuario.Uuid),
            ("T_EMPRESAS", "UUID_EMPRESAS", cliente.Uuid),
            ("T_USUARIOS_EMPRESAS", "UUID_USUARIOS_EMPRESAS", cliente.Accesos.Single().Uuid),
        };
        foreach (var (tabla, llave, uuid) in creadas)
        {
            var auditoria = await LeerAsync(cadena, sesionDelCreador, tabla, llave, uuid, token);
            Assert.True(auditoria.FechaCreacion == Creacion, $"{tabla}.FECHA_CREACION es {auditoria.FechaCreacion:O} y debe ser {Creacion:O} (la hora del reloj).");
            Assert.True(auditoria.UsuarioCreacion == creador, $"{tabla}.UUID_USUARIOS_CREACION no es el usuario de la sesión.");
            Assert.Null(auditoria.FechaModificacion);
            Assert.Null(auditoria.UsuarioModificacion);
        }

        // Al modificar: se llenan las columnas de modificación y no se tocan las de creación. Modifica otro
        // usuario de la misma firma, que también tiene acceso al cliente (sin acceso, la seguridad por fila no
        // le dejaría ver ni cambiar el cliente).
        var modificador = segundoUsuario.Uuid;
        var sesionDelModificador = new SesionSql(modificador, firma.Uuid);
        fabrica.Sesion.Actuar(sesionDelModificador);
        fabrica.Reloj.Ahora = new DateTimeOffset(Modificacion);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
            var existente = await contexto.Firmas.SingleAsync(f => f.Uuid == firma.Uuid, token);
            contexto.Entry(existente).Property(nameof(Firma.Nombre)).CurrentValue = "Nombre nuevo";
            await contexto.SaveChangesAsync(token);
        }

        var modificada = await LeerAsync(cadena, sesionDelModificador, "T_FIRMAS", "UUID_FIRMAS", firma.Uuid, token);
        Assert.True(modificada.FechaModificacion == Modificacion, $"T_FIRMAS.FECHA_MODIFICACION es {modificada.FechaModificacion:O} y debe ser {Modificacion:O}.");
        Assert.Equal(modificador, modificada.UsuarioModificacion);
        Assert.True(modificada.FechaCreacion == Creacion, "La modificación cambió FECHA_CREACION.");
        Assert.Equal(creador, modificada.UsuarioCreacion);

        // Una fila que no se tocó conserva su auditoría de creación.
        var intacta = await LeerAsync(cadena, sesionDelModificador, "T_USUARIOS", "UUID_USUARIOS", usuario.Uuid, token);
        Assert.Null(intacta.FechaModificacion);

        // Modificación de un cliente, que vive en otro contexto (ClientesDbContext) y otro módulo.
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var existente = await contexto.Clientes.SingleAsync(c => c.Uuid == cliente.Uuid, token);
            contexto.Entry(existente).Property(nameof(existente.RazonSocial)).CurrentValue = "Razón social nueva";
            await contexto.SaveChangesAsync(token);
        }

        var cambiado = await LeerAsync(cadena, sesionDelModificador, "T_EMPRESAS", "UUID_EMPRESAS", cliente.Uuid, token);
        Assert.True(cambiado.FechaModificacion == Modificacion);
        Assert.Equal(modificador, cambiado.UsuarioModificacion);
        Assert.True(cambiado.FechaCreacion == Creacion);
        Assert.Equal(creador, cambiado.UsuarioCreacion);
    }

    [Fact]
    public async Task Auditoria_UsaDatetime2ConPrecisionTresEnLasCuatroTablas()
    {
        // Consulta el catálogo (INFORMATION_SCHEMA), no filas de las tablas: no necesita sesión.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);

        var filas = await ConsultasSql.FilasAsync(
            cadena,
            """
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, DATETIME_PRECISION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME IN ('T_FIRMAS', 'T_USUARIOS', 'T_EMPRESAS', 'T_USUARIOS_EMPRESAS')
              AND COLUMN_NAME IN ('FECHA_CREACION', 'FECHA_MODIFICACION')
            """,
            token);

        Assert.Equal(8, filas.Count);
        foreach (var fila in filas)
        {
            Assert.True(
                (string)fila[2]! == "datetime2" && Convert.ToInt32(fila[3]) == 3,
                $"{fila[0]}.{fila[1]} es {fila[2]}({fila[3]}) y debe ser DATETIME2(3).");
        }
    }

    [Fact]
    public async Task Auditoria_GuardaLaHoraEnUtcAunqueElRelojTengaOtraZona()
    {
        // El reloj marca hora de Colombia (UTC-5); lo guardado debe ser el mismo instante en UTC.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var instante = new DateTimeOffset(2031, 6, 7, 8, 9, 10, 500, TimeSpan.FromHours(-5));
        fabrica.Reloj.Ahora = instante;

        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);

        var auditoria = await LeerAsync(cadena, new SesionSql(usuario.Uuid, firma.Uuid), "T_FIRMAS", "UUID_FIRMAS", firma.Uuid, token);
        Assert.Equal(instante.UtcDateTime, DateTime.SpecifyKind(auditoria.FechaCreacion, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Auditoria_SinContextoDeSesion_NoGuarda()
    {
        // Sin FabricaNitido.Sesion: el host queda con su IContextoSesion de producción, que sin petición HTTP
        // entrega Guid.Empty. El error ahora sale de la regla «usuario vacío» del interceptor de auditoría.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena, usarSesionDePrueba: false);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        var firma = Firma.Crear($"Sin sesión {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        contexto.Add(firma);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => contexto.SaveChangesAsync(token));

        Assert.Contains("No se puede guardar sin saber qué usuario actúa", error.Message, StringComparison.Ordinal);
        Assert.Contains("IContextoSesion", error.Message, StringComparison.Ordinal);
        await AssertNoSeGuardoAsync(cadena, firma.Uuid, token);
    }

    [Fact]
    public async Task Auditoria_SinContextoDeSesion_SaveChangesSincrono_NoGuarda()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena, usarSesionDePrueba: false);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        var firma = Firma.Crear($"Sin sesión síncrono {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        contexto.Add(firma);

        var error = Assert.Throws<InvalidOperationException>(() => contexto.SaveChanges());

        Assert.Contains("No se puede guardar sin saber qué usuario actúa", error.Message, StringComparison.Ordinal);
        Assert.Contains("IContextoSesion", error.Message, StringComparison.Ordinal);
        await AssertNoSeGuardoAsync(cadena, firma.Uuid, token);
    }

    [Fact]
    public async Task Auditoria_ConUsuarioVacio_NoGuarda()
    {
        // Decisión 8 del ADR 0003: desde F0-02b el host siempre registra un IContextoSesion, así que «no hay
        // contexto» ya no existe; lo que existe es un contexto sin usuario (Guid.Empty = sin sesión). El interceptor
        // de auditoría se niega a guardar con el mismo error.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var firma = Firma.Crear($"Usuario vacío {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        fabrica.Sesion.Actuar(Guid.Empty, firma.Uuid); // la firma es la correcta; falta el usuario
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(firma);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => contexto.SaveChangesAsync(token));

        Assert.Contains("No se puede guardar sin saber qué usuario actúa", error.Message, StringComparison.Ordinal);
        Assert.Contains("IContextoSesion", error.Message, StringComparison.Ordinal);
        await AssertNoSeGuardoAsync(cadena, firma.Uuid, token);
    }

    [Fact]
    public async Task Auditoria_ConUsuarioVacio_SaveChangesSincrono_NoGuarda()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var firma = Firma.Crear($"Usuario vacío síncrono {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        fabrica.Sesion.Actuar(Guid.Empty, firma.Uuid);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(firma);

        var error = Assert.Throws<InvalidOperationException>(() => contexto.SaveChanges());

        Assert.Contains("No se puede guardar sin saber qué usuario actúa", error.Message, StringComparison.Ordinal);
        Assert.Contains("IContextoSesion", error.Message, StringComparison.Ordinal);
        await AssertNoSeGuardoAsync(cadena, firma.Uuid, token);
    }

    /// <summary>
    /// «No se guardó», leído con la sesión de esa firma (la única que podría ver la fila si existiera). Lleva su
    /// control positivo: la misma lectura, con el mismo tipo de sesión, sí ve una firma que se guardó de verdad.
    /// Sin el control, la aserción pasaría en vacío con cualquier sesión que no viera nada.
    /// </summary>
    private static async Task AssertNoSeGuardoAsync(string cadena, Guid uuidFirmas, CancellationToken token)
    {
        const string Sql = "SELECT COUNT(*) FROM T_FIRMAS WHERE UUID_FIRMAS = @llave";

        await using var control = new FabricaNitido(cadena);
        var (firmaGuardada, usuarioGuardado) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(control, token);
        var vistaDelControl = await ConsultasSql.EscalarAsync(
            cadena, new SesionSql(usuarioGuardado.Uuid, firmaGuardada.Uuid), Sql, token, ("@llave", firmaGuardada.Uuid));
        Assert.Equal(1, Convert.ToInt32(vistaDelControl));

        var guardadas = await ConsultasSql.EscalarAsync(
            cadena, new SesionSql(Guid.CreateVersion7(), uuidFirmas), Sql, token, ("@llave", uuidFirmas));
        Assert.Equal(0, Convert.ToInt32(guardadas));
    }

    [Fact]
    public async Task Auditoria_SaveChangesSincrono_AuditaYGuarda()
    {
        // La ruta síncrona del interceptor (SavingChanges) es distinta de la asíncrona: también audita.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var creador = Guid.CreateVersion7();
        var modificador = Guid.CreateVersion7();
        var momentoDeCreacion = new DateTime(2032, 4, 5, 6, 7, 8, 321, DateTimeKind.Utc);
        var momentoDeModificacion = new DateTime(2032, 4, 6, 10, 11, 12, 654, DateTimeKind.Utc);
        fabrica.Reloj.Ahora = new DateTimeOffset(momentoDeCreacion);
        var firma = Firma.Crear($"Firma síncrona {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        var usuario = DatosDePrueba.NuevoUsuario(firma.Uuid);
        fabrica.Sesion.Actuar(creador, firma.Uuid);

        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
            contexto.Add(firma);
            contexto.Add(usuario);
            var filas = contexto.SaveChanges();
            Assert.Equal(2, filas);
        }

        foreach (var (tabla, llave, uuid) in new[] { ("T_FIRMAS", "UUID_FIRMAS", firma.Uuid), ("T_USUARIOS", "UUID_USUARIOS", usuario.Uuid) })
        {
            var creada = await LeerAsync(cadena, new SesionSql(creador, firma.Uuid), tabla, llave, uuid, token);
            Assert.True(creada.FechaCreacion == momentoDeCreacion, $"{tabla}.FECHA_CREACION es {creada.FechaCreacion:O} y debe ser {momentoDeCreacion:O}.");
            Assert.Equal(creador, creada.UsuarioCreacion);
            Assert.Null(creada.FechaModificacion);
            Assert.Null(creada.UsuarioModificacion);
        }

        fabrica.Sesion.Actuar(modificador, firma.Uuid);
        fabrica.Reloj.Ahora = new DateTimeOffset(momentoDeModificacion);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
            var existente = contexto.Firmas.Single(f => f.Uuid == firma.Uuid);
            contexto.Entry(existente).Property(nameof(Firma.Nombre)).CurrentValue = "Nombre síncrono nuevo";
            Assert.Equal(1, contexto.SaveChanges());
        }

        var modificada = await LeerAsync(cadena, new SesionSql(modificador, firma.Uuid), "T_FIRMAS", "UUID_FIRMAS", firma.Uuid, token);
        Assert.True(modificada.FechaModificacion == momentoDeModificacion, $"T_FIRMAS.FECHA_MODIFICACION es {modificada.FechaModificacion:O} y debe ser {momentoDeModificacion:O}.");
        Assert.Equal(modificador, modificada.UsuarioModificacion);
        Assert.True(modificada.FechaCreacion == momentoDeCreacion, "La modificación cambió FECHA_CREACION.");
        Assert.Equal(creador, modificada.UsuarioCreacion);
    }

    [Fact]
    public async Task Auditoria_LaFechaSaleDelReloj_NoDelSistema()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        fabrica.Reloj.Ahora = new DateTimeOffset(2041, 1, 2, 3, 4, 5, 6, TimeSpan.Zero);

        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);

        var auditoria = await LeerAsync(cadena, new SesionSql(usuario.Uuid, firma.Uuid), "T_FIRMAS", "UUID_FIRMAS", firma.Uuid, token);
        Assert.Equal(2041, auditoria.FechaCreacion.Year);
    }
}
