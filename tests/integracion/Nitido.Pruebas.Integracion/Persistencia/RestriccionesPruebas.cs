using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Comportamiento de las restricciones de los criterios de aceptación, contra la base migrada: el rol, el
/// correo único, el documento único por firma, el acceso único y la ausencia de cascadas. También el
/// historial de las tablas temporales y un viaje de ida y vuelta por EF.
/// </summary>
public class RestriccionesPruebas(ContenedorSqlServer contenedor)
{
    private static async Task<SqlException> EsperarFallaDeSqlAsync(Func<Task> accion)
    {
        var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(accion);
        return ex.GetBaseException() as SqlException ?? throw new Xunit.Sdk.XunitException("La falla no vino de SQL Server: " + ex);
    }

    private async Task<FabricaNitido> NuevaFabricaAsync(CancellationToken token) =>
        new(await contenedor.CadenaDeBaseMigradaAsync(token));

    [Fact]
    public async Task Rol_SoloAdmiteAdministrador()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fabrica = await NuevaFabricaAsync(token);
        var (firma, _) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(DatosDePrueba.NuevoUsuario(firma.Uuid, rol: "AUXILIAR"));

        var falla = await EsperarFallaDeSqlAsync(() => contexto.SaveChangesAsync(token));
        Assert.Equal(547, falla.Number); // violación de restricción CHECK / FOREIGN KEY
        Assert.Contains("CK_T_USUARIOS_ROL", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correo_UnCorreoPerteneceAUnaSolaFirma()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fabrica = await NuevaFabricaAsync(token);
        var (_, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var (otraFirma, _) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        // Mismo correo en otra firma, escrito con otras mayúsculas: el normalizado es el mismo.
        contexto.Add(DatosDePrueba.NuevoUsuario(otraFirma.Uuid, correo: usuario.Correo.ToUpperInvariant()));

        var falla = await EsperarFallaDeSqlAsync(() => contexto.SaveChangesAsync(token));
        Assert.True(falla.Number is 2601 or 2627, $"Se esperaba violación de índice único y fue el error {falla.Number}.");
        Assert.Contains("UX_T_USUARIOS_CORREO_NORMALIZADO", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Documento_UnClienteNoSeRepiteEnLaMismaFirmaPeroSiEnOtra()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fabrica = await NuevaFabricaAsync(token);
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var (otraFirma, otroUsuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var nit = DatosDePrueba.NitUnico();
        await DatosDePrueba.GuardarAsync(fabrica, DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid, nit), token);

        // Misma firma y mismo documento: no.
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            contexto.Add(DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid, nit));
            var falla = await EsperarFallaDeSqlAsync(() => contexto.SaveChangesAsync(token));
            Assert.Contains("UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO", falla.Message, StringComparison.Ordinal);
        }

        // Otra firma con el mismo documento: sí.
        await DatosDePrueba.GuardarAsync(fabrica, DatosDePrueba.NuevoCliente(fabrica, otraFirma.Uuid, otroUsuario.Uuid, nit), token);
    }

    [Fact]
    public async Task LlavesEntreModulos_NoAceptanFirmaNiUsuarioInexistentes()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fabrica = await NuevaFabricaAsync(token);
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);

        // Firma que no existe: FK_T_EMPRESAS_T_FIRMAS (creada a mano en CrearClientes). La sesión es la de esa firma
        // inexistente a propósito: así el BLOCK de seguridad por fila deja pasar la fila y quien la rechaza es la
        // llave foránea (con la sesión de la firma real, el BLOCK respondería antes y la prueba no probaría la llave).
        var firmaInexistente = Guid.CreateVersion7();
        using (var alcance = fabrica.Services.CreateScope())
        {
            fabrica.Sesion.Actuar(usuario.Uuid, firmaInexistente);
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            contexto.Add(DatosDePrueba.NuevoCliente(fabrica, firmaInexistente, usuario.Uuid));
            var falla = await EsperarFallaDeSqlAsync(() => contexto.SaveChangesAsync(token));
            Assert.Equal(547, falla.Number);
            Assert.Contains("FK_T_EMPRESAS_T_FIRMAS", falla.Message, StringComparison.Ordinal);
        }

        // Usuario que no existe: FK_T_USUARIOS_EMPRESAS_T_USUARIOS. Sesión del usuario inexistente, por la misma
        // razón: el BLOCK de T_USUARIOS_EMPRESAS compara el usuario del acceso con el de la sesión.
        var usuarioInexistente = Guid.CreateVersion7();
        using (var alcance = fabrica.Services.CreateScope())
        {
            fabrica.Sesion.Actuar(usuarioInexistente, firma.Uuid);
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            contexto.Add(DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuarioInexistente));
            var falla = await EsperarFallaDeSqlAsync(() => contexto.SaveChangesAsync(token));
            Assert.Equal(547, falla.Number);
            Assert.Contains("FK_T_USUARIOS_EMPRESAS_T_USUARIOS", falla.Message, StringComparison.Ordinal);
        }

        // Control positivo: con una firma y un usuario que sí existen, el mismo guardado funciona.
        await DatosDePrueba.GuardarAsync(fabrica, DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid), token);
    }

    [Fact]
    public async Task AccesoDuplicado_AUnMismoClienteNoSePermite()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var cliente = DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid);
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);

        // El mismo usuario otra vez sobre el mismo cliente, por SQL directo (el dominio aún no tiene «dar acceso»).
        // Con la sesión de ese usuario y esa firma: es la única que pasa los BLOCK, así que quien rechaza la fila
        // es el índice único y no la seguridad por fila.
        var sesion = new SesionSql(usuario.Uuid, firma.Uuid);
        var falla = await Assert.ThrowsAnyAsync<SqlException>(() =>
            InsertsSql.EjecutarAsync(cadena, sesion, InsertsSql.Acceso(firma.Uuid, cliente.Uuid, usuario.Uuid, usuario.Uuid), token));
        Assert.True(falla.Number is 2601 or 2627, $"Se esperaba violación de índice único y fue el error {falla.Number}.");
        Assert.Contains("UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS", falla.Message, StringComparison.Ordinal);

        // Control positivo: el acceso original sigue ahí, uno solo.
        var delCliente = await ConsultasSql.EscalarAsync(
            cadena, sesion, "SELECT COUNT(*) FROM T_USUARIOS_EMPRESAS WHERE UUID_EMPRESAS = @cliente", token, ("@cliente", cliente.Uuid));
        Assert.Equal(1, Convert.ToInt32(delCliente));
    }

    [Fact]
    public async Task SinCascada_NoSeBorraUnaFirmaConUsuariosNiUnClienteConAccesos()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var cliente = DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid);
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);

        // Con la sesión de su dueño, para que la seguridad por fila deje ver las filas: sin sesión el DELETE
        // afectaría 0 filas, no fallaría, y la prueba pasaría en vacío. Lo que las frena es la llave foránea.
        var sesion = new SesionSql(usuario.Uuid, firma.Uuid);

        var borrarCliente = await Assert.ThrowsAnyAsync<SqlException>(() =>
            ConsultasSql.EjecutarAsync(cadena, sesion, "DELETE FROM T_EMPRESAS WHERE UUID_EMPRESAS = @llave", token, ("@llave", cliente.Uuid)));
        Assert.Equal(547, borrarCliente.Number);

        var borrarUsuario = await Assert.ThrowsAnyAsync<SqlException>(() =>
            ConsultasSql.EjecutarAsync(cadena, sesion, "DELETE FROM T_USUARIOS WHERE UUID_USUARIOS = @llave", token, ("@llave", usuario.Uuid)));
        Assert.Equal(547, borrarUsuario.Number);

        var borrarFirma = await Assert.ThrowsAnyAsync<SqlException>(() =>
            ConsultasSql.EjecutarAsync(cadena, sesion, "DELETE FROM T_FIRMAS WHERE UUID_FIRMAS = @llave", token, ("@llave", firma.Uuid)));
        Assert.Equal(547, borrarFirma.Number);

        // Y nada se borró.
        Assert.Equal(1, Convert.ToInt32(await ConsultasSql.EscalarAsync(
            cadena, sesion, "SELECT COUNT(*) FROM T_EMPRESAS WHERE UUID_EMPRESAS = @llave", token, ("@llave", cliente.Uuid))));
        Assert.Equal(1, Convert.ToInt32(await ConsultasSql.EscalarAsync(
            cadena, sesion, "SELECT COUNT(*) FROM T_USUARIOS WHERE UUID_USUARIOS = @llave", token, ("@llave", usuario.Uuid))));
        Assert.Equal(1, Convert.ToInt32(await ConsultasSql.EscalarAsync(
            cadena, sesion, "SELECT COUNT(*) FROM T_FIRMAS WHERE UUID_FIRMAS = @llave", token, ("@llave", firma.Uuid))));
    }

    [Fact]
    public async Task IdaYVuelta_ElClienteVuelveConSusDatosYSuAcceso()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fabrica = await NuevaFabricaAsync(token);
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var cliente = DatosDePrueba.NuevoCliente(fabrica, firma.Uuid, usuario.Uuid);
        await DatosDePrueba.GuardarAsync(fabrica, cliente, token);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
        var leido = await contexto.Clientes.Include(c => c.Accesos).SingleAsync(c => c.Uuid == cliente.Uuid, token);

        Assert.Equal(firma.Uuid, leido.UuidFirmas);
        Assert.Equal("31", leido.TipoDocumento);
        Assert.Equal(cliente.NumeroDocumento, leido.NumeroDocumento);
        Assert.Equal("7", leido.DigitoVerificacion);
        Assert.Equal("Ferretería El Tornillo S.A.S.", leido.RazonSocial);
        Assert.True(leido.Activo);
        var acceso = Assert.Single(leido.Accesos);
        Assert.Equal(usuario.Uuid, acceso.UuidUsuarios);
        Assert.Equal(firma.Uuid, acceso.UuidFirmas); // F0-02b: el acceso lleva la firma de su cliente
        Assert.Equal(cliente.Accesos.Single().Uuid, acceso.Uuid);

        // Y la firma, por el otro contexto.
        var firmaLeida = await alcance.ServiceProvider.GetRequiredService<FirmasDbContext>()
            .Firmas.SingleAsync(f => f.Uuid == firma.Uuid, token);
        Assert.Equal(firma.Nombre, firmaLeida.Nombre);
    }

    [Fact]
    public async Task Historial_GuardaLaVersionAnteriorAlModificar()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var fabrica = new FabricaNitido(cadena);
        var (firma, usuario) = await DatosDePrueba.GuardarFirmaYUsuarioAsync(fabrica, token);
        var sesion = new SesionSql(usuario.Uuid, firma.Uuid);

        var antes = await ConsultasSql.EscalarAsync(
            cadena, sesion, "SELECT COUNT(*) FROM T_FIRMAS_HISTORIAL WHERE UUID_FIRMAS = @llave", token, ("@llave", firma.Uuid));
        Assert.Equal(0, Convert.ToInt32(antes));

        await Task.Delay(50, token); // que las dos transacciones no caigan en el mismo milisegundo
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
            var existente = await contexto.Firmas.SingleAsync(f => f.Uuid == firma.Uuid, token);
            contexto.Entry(existente).Property(nameof(Firma.Nombre)).CurrentValue = "Nombre cambiado";
            await contexto.SaveChangesAsync(token);
        }

        var historial = await ConsultasSql.FilasAsync(
            cadena,
            sesion,
            "SELECT NOMBRE, PERIODO_INICIO, PERIODO_FIN FROM T_FIRMAS_HISTORIAL WHERE UUID_FIRMAS = @llave",
            token,
            ("@llave", firma.Uuid));
        var version = Assert.Single(historial);
        Assert.Equal(firma.Nombre, version[0]);
        Assert.True((DateTime)version[1]! < (DateTime)version[2]!, "El periodo de la versión anterior debe tener duración.");
    }

    [Fact]
    public async Task Firma_NoAdmiteLlaveVacia()
    {
        // Decisión 3 del ADR 0003: sin sesión, las tres llaves de SESSION_CONTEXT valen Guid.Empty. Si existiera
        // una firma con llave vacía, el predicado la dejaría ver a toda conexión sin sesión. Lo impide el CHECK.
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        var sinSesion = SesionSql.SinSesion;
        const string Insertar =
            "INSERT INTO T_FIRMAS (UUID_FIRMAS, NOMBRE, FECHA_CREACION, UUID_USUARIOS_CREACION) VALUES (@llave, N'Firma de prueba', SYSUTCDATETIME(), @creador)";

        // Con la sesión vacía el BLOCK dejaría pasar la fila (Guid.Empty = Guid.Empty): solo el CHECK la frena.
        var falla = await Assert.ThrowsAnyAsync<SqlException>(() =>
            ConsultasSql.EjecutarAsync(cadena, sinSesion, Insertar, token, ("@llave", Guid.Empty), ("@creador", Guid.CreateVersion7())));
        Assert.Equal(547, falla.Number);
        Assert.Contains("CK_T_FIRMAS_UUID_FIRMAS", falla.Message, StringComparison.Ordinal);

        // Control positivo: la misma orden con una llave válida y la sesión de esa firma sí inserta.
        var firma = Guid.CreateVersion7();
        var insertadas = await ConsultasSql.EjecutarAsync(
            cadena, new SesionSql(Guid.CreateVersion7(), firma), Insertar, token, ("@llave", firma), ("@creador", Guid.CreateVersion7()));
        Assert.Equal(1, insertadas);
    }
}
