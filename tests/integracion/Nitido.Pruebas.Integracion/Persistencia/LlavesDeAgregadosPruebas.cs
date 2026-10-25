using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Cómo trata EF Core las llaves GUID al guardar. Regresión del hallazgo M1 de la revisión de F0-02a: si toda
/// llave <c>Guid</c> es <c>ValueGenerated.OnAdd</c>, un hijo nuevo con la llave que ya trae la fábrica del
/// dominio, agregado a un agregado cargado, queda <c>Modified</c> en vez de <c>Added</c> y el guardado falla con
/// <see cref="DbUpdateConcurrencyException"/> (UPDATE de 0 filas). Reglas que se exigen aquí:
/// la llave que trae la fábrica se conserva; la llave vacía recibe un GUID versión 7 al agregarse.
/// Desde F0-02b cada guardado va con la sesión de quien lo hace, y como un usuario solo ve y crea sus propios
/// accesos, las lecturas con SQL directo se hacen con la sesión del dueño de cada fila.
/// </summary>
public class LlavesDeAgregadosPruebas(ContenedorSqlServer contenedor)
{
    private static readonly DateTimeOffset MomentoDeCreacion = new(2033, 5, 6, 7, 8, 9, 123, TimeSpan.Zero);
    private static readonly DateTimeOffset MomentoDeLaFabricaDelAcceso = new(2033, 5, 7, 1, 2, 3, 45, TimeSpan.Zero);
    private static readonly DateTimeOffset MomentoDeGuardarElAcceso = new(2033, 5, 8, 14, 15, 16, 789, TimeSpan.Zero);

    /// <summary>
    /// Accesos de un cliente, sumando el que cada dueño ve de sí mismo: la política de <c>T_USUARIOS_EMPRESAS</c>
    /// deja ver a cada usuario solo sus accesos, así que el total es la suma de lo que ve cada dueño. Cada
    /// consulta pide además explícitamente el acceso de su propia sesión, para que el conteo no dependa de que la
    /// política esté o no activa.
    /// </summary>
    private static async Task<int> ContarAccesosAsync(string cadena, Guid uuidCliente, CancellationToken token, params SesionSql[] duenos)
    {
        var total = 0;
        foreach (var sesion in duenos)
        {
            total += Convert.ToInt32(await ConsultasSql.EscalarAsync(
                cadena,
                sesion,
                "SELECT COUNT(*) FROM T_USUARIOS_EMPRESAS WHERE UUID_EMPRESAS = @cliente AND UUID_USUARIOS = @usuario",
                token,
                ("@cliente", uuidCliente),
                ("@usuario", sesion.UuidUsuarios)));
        }

        return total;
    }

    [Fact]
    public async Task Hijo_NuevoSobreAgregadoCargado_SeInserta()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        fabrica.Reloj.Ahora = MomentoDeCreacion;

        // Firma, dos usuarios y un cliente con su primer acceso (el del primer usuario).
        var (firma, primerUsuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var creador = primerUsuario.Uuid;
        var segundoUsuario = await DatosDePrueba.GuardarOtroUsuarioAsync(fabrica, firma.Uuid, token);
        var sesionDelPrimero = new SesionSql(primerUsuario.Uuid, firma.Uuid);
        var sesionDelSegundo = new SesionSql(segundoUsuario.Uuid, firma.Uuid);
        var cliente = DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, primerUsuario.Uuid);
        var primerAcceso = cliente.Accesos.Single();
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);
        Assert.Equal(1, await ContarAccesosAsync(cadena, cliente.Uuid, token, sesionDelPrimero, sesionDelSegundo));

        // Un contexto NUEVO carga el cliente con sus accesos (lo carga el primer usuario, que tiene acceso) y le
        // agrega el segundo acceso, que se guarda con la sesión del segundo usuario (el BLOCK exige que el acceso
        // sea del usuario de la sesión). La llave la trae la fábrica del dominio (GUID v7 con otra hora distinta
        // de la del guardado).
        var modificador = segundoUsuario.Uuid;
        fabrica.Reloj.Ahora = MomentoDeGuardarElAcceso;
        var segundoAcceso = DatosDePrueba.NuevoAccesoDeLaFabrica(firma.Uuid, segundoUsuario.Uuid, MomentoDeLaFabricaDelAcceso);
        var llaveDeLaFabrica = segundoAcceso.Uuid;
        GuidVersion7Ayuda.AssertEsVersion7Con(llaveDeLaFabrica, MomentoDeLaFabricaDelAcceso, "AccesoCliente.Crear");
        Assert.Equal(firma.Uuid, segundoAcceso.UuidFirmas);

        EntityState estadoAntesDeGuardar;
        using (var alcance = fabrica.Services.CreateScope())
        {
            fabrica.Sesion.Actuar(sesionDelPrimero);
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var cargado = await contexto.Clientes.Include(c => c.Accesos).SingleAsync(c => c.Uuid == cliente.Uuid, token);
            Assert.Equal(primerAcceso.Uuid, Assert.Single(cargado.Accesos).Uuid);

            fabrica.Sesion.Actuar(sesionDelSegundo);
            DatosDePrueba.AgregarAcceso(cargado, segundoAcceso);
            contexto.ChangeTracker.DetectChanges();
            estadoAntesDeGuardar = contexto.Entry(segundoAcceso).State;

            // Con el defecto M1 esta línea lanza DbUpdateConcurrencyException («expected to affect 1 row(s), but
            // actually affected 0»): EF hace un UPDATE de una fila que no existe.
            await contexto.SaveChangesAsync(token);
        }

        Assert.Equal(EntityState.Added, estadoAntesDeGuardar);

        // Quedó en la base: dos accesos (uno que ve cada dueño), el nuevo con la llave de la fábrica (v7 con la
        // hora de la fábrica).
        Assert.Equal(2, await ContarAccesosAsync(cadena, cliente.Uuid, token, sesionDelPrimero, sesionDelSegundo));
        Assert.Equal(llaveDeLaFabrica, segundoAcceso.Uuid);
        var guardada = await ConsultasSql.FilasAsync(
            cadena,
            sesionDelSegundo,
            "SELECT UUID_USUARIOS_EMPRESAS, UUID_USUARIOS, UUID_FIRMAS FROM T_USUARIOS_EMPRESAS WHERE UUID_EMPRESAS = @cliente AND UUID_USUARIOS = @usuario",
            token,
            ("@cliente", cliente.Uuid),
            ("@usuario", segundoUsuario.Uuid));
        var fila = Assert.Single(guardada);
        GuidVersion7Ayuda.AssertEsVersion7Con((Guid)fila[0]!, MomentoDeLaFabricaDelAcceso, "T_USUARIOS_EMPRESAS.UUID_USUARIOS_EMPRESAS del segundo acceso");
        Assert.Equal(llaveDeLaFabrica, (Guid)fila[0]!);
        Assert.Equal(firma.Uuid, (Guid)fila[2]!); // el acceso guarda la firma de su cliente

        // Auditoría del segundo acceso: creación con la hora y el usuario del guardado, sin modificación.
        var auditoria = await AuditoriaDeFila.LeerAsync(cadena, sesionDelSegundo, "T_USUARIOS_EMPRESAS", "UUID_USUARIOS_EMPRESAS", llaveDeLaFabrica, token);
        Assert.True(
            auditoria.FechaCreacion == MomentoDeGuardarElAcceso.UtcDateTime,
            $"FECHA_CREACION es {auditoria.FechaCreacion:O} y debe ser {MomentoDeGuardarElAcceso.UtcDateTime:O} (la hora del reloj al guardar).");
        Assert.Equal(modificador, auditoria.UsuarioCreacion);
        Assert.Null(auditoria.FechaModificacion);
        Assert.Null(auditoria.UsuarioModificacion);

        // Lo que ya existía no se tocó: ni el primer acceso ni el cliente.
        var anterior = await AuditoriaDeFila.LeerAsync(cadena, sesionDelPrimero, "T_USUARIOS_EMPRESAS", "UUID_USUARIOS_EMPRESAS", primerAcceso.Uuid, token);
        Assert.True(anterior.FechaCreacion == MomentoDeCreacion.UtcDateTime);
        Assert.Equal(creador, anterior.UsuarioCreacion);
        Assert.Null(anterior.FechaModificacion);
        var delCliente = await AuditoriaDeFila.LeerAsync(cadena, sesionDelPrimero, "T_EMPRESAS", "UUID_EMPRESAS", cliente.Uuid, token);
        Assert.Null(delCliente.FechaModificacion);
        Assert.Null(delCliente.UsuarioModificacion);
    }

    [Fact]
    public async Task Entidad_ConLlaveVacia_RecibeGuidV7AlAgregar()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var creador = Guid.CreateVersion7();
        fabrica.Reloj.Ahora = MomentoDeCreacion;
        var firma = Firma.Crear($"Firma {Guid.NewGuid():N}", fabrica.Reloj.GetUtcNow());
        var usuario = DatosDePrueba.NuevoUsuario(firma.Uuid);
        Assert.Equal(Guid.Empty, usuario.Uuid);
        fabrica.Sesion.Actuar(creador, firma.Uuid);

        // Una entidad que llega con la llave vacía a un contexto que ya tiene otra entidad guardada.
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(firma);
        await contexto.SaveChangesAsync(token);

        fabrica.Reloj.Ahora = MomentoDeGuardarElAcceso;
        contexto.Add(usuario);
        await contexto.SaveChangesAsync(token);

        GuidVersion7Ayuda.AssertEsVersion7Con(usuario.Uuid, MomentoDeGuardarElAcceso, "Usuario con llave vacía");
        var auditoria = await AuditoriaDeFila.LeerAsync(cadena, new SesionSql(creador, firma.Uuid), "T_USUARIOS", "UUID_USUARIOS", usuario.Uuid, token);
        Assert.True(
            auditoria.FechaCreacion == MomentoDeGuardarElAcceso.UtcDateTime,
            $"FECHA_CREACION es {auditoria.FechaCreacion:O} y debe ser {MomentoDeGuardarElAcceso.UtcDateTime:O}.");
        Assert.Equal(creador, auditoria.UsuarioCreacion);
        Assert.Null(auditoria.FechaModificacion);
        Assert.Null(auditoria.UsuarioModificacion);
    }

    [Fact]
    public async Task Entidad_ConLlaveDeLaFabrica_ConservaSuLlave()
    {
        // Complementa Identificadores_LaLlaveQueLlegaDeLaFabricaNoSeReemplaza (que cubre una Firma): aquí un
        // agregado de dos niveles, Cliente y su acceso, con llaves de una hora distinta de la del reloj.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        fabrica.Reloj.Ahora = MomentoDeCreacion;
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var sesion = new SesionSql(usuario.Uuid, firma.Uuid);

        var cliente = Cliente.Crear(
            firma.Uuid, "31", DatosDePrueba.NitUnico(), "7", "Cliente con llave de la fábrica", usuario.Uuid, MomentoDeLaFabricaDelAcceso);
        var llaveDelCliente = cliente.Uuid;
        var llaveDelAcceso = cliente.Accesos.Single().Uuid;
        fabrica.Reloj.Ahora = MomentoDeGuardarElAcceso;
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);

        Assert.Equal(llaveDelCliente, cliente.Uuid);
        Assert.Equal(llaveDelAcceso, cliente.Accesos.Single().Uuid);
        var llaveCliente = await ConsultasSql.EscalarAsync(
            cadena, sesion, "SELECT UUID_EMPRESAS FROM T_EMPRESAS WHERE UUID_EMPRESAS = @llave", token, ("@llave", llaveDelCliente));
        var llaveAcceso = await ConsultasSql.EscalarAsync(
            cadena, sesion, "SELECT UUID_USUARIOS_EMPRESAS FROM T_USUARIOS_EMPRESAS WHERE UUID_USUARIOS_EMPRESAS = @llave", token, ("@llave", llaveDelAcceso));
        Assert.NotNull(llaveCliente);
        Assert.NotNull(llaveAcceso);
        GuidVersion7Ayuda.AssertEsVersion7Con((Guid)llaveCliente, MomentoDeLaFabricaDelAcceso, "T_EMPRESAS.UUID_EMPRESAS");
        GuidVersion7Ayuda.AssertEsVersion7Con((Guid)llaveAcceso, MomentoDeLaFabricaDelAcceso, "T_USUARIOS_EMPRESAS.UUID_USUARIOS_EMPRESAS");
    }
}
