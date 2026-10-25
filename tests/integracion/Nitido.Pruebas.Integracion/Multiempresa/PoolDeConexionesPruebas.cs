using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// El pool de conexiones no puede arrastrar el contexto de una petición a la siguiente (ADR 0003). Con
/// <c>@read_only = 1</c> una llave no se puede cambiar dentro de la misma conexión lógica; lo que impide que la
/// firma A se le quede pegada a la petición de la firma B es que SQL Server restablece la sesión
/// (<c>sp_reset_connection</c>) cuando el pool entrega una conexión ya usada, y ese restablecimiento viaja con la
/// primera orden, que es justo la del interceptor. Si no limpiara las llaves de solo lectura, la segunda apertura
/// fallaría con el error 15664: falla cerrado, nunca deja ver datos ajenos.
/// </summary>
public class PoolDeConexionesPruebas(ContenedorSqlServer contenedor)
{
    private const int ErrorDeLlaveDeSoloLectura = 15664;

    [Fact]
    public async Task Pool_ConexionReutilizadaNoConservaContexto()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseMigradaAsync(token);
        await using var siembra = new FabricaNitido(cadena);
        var s = await SiembraDeDosFirmas.CrearAsync(siembra, cadena, token);

        // Un pool propio (el nombre de la aplicación distingue el pool) con una sola conexión: la reutilización
        // es segura, y si el pool hiciera falta una segunda conexión, la prueba lo sabría porque se agotaría.
        var cadenaDelPool = new SqlConnectionStringBuilder(cadena)
        {
            Pooling = true,
            MaxPoolSize = 1,
            ApplicationName = $"nitido-pool-{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var fabrica = new FabricaNitido(cadenaDelPool);

        // 1. Alcance 1: firma A, usuario A1, cliente CA1.
        var primera = await LeerAsync(fabrica, new SesionSql(s.A1, s.FirmaA, s.CA1), token);
        Assert.Equal(s.A1, primera.UuidUsuarios);
        Assert.Equal(s.FirmaA, primera.UuidFirmas);
        Assert.Equal(s.CA1, primera.UuidEmpresas);

        // 2. Alcance 2: firma B, usuario B1, cliente CB1, SIN el error 15664 y en la MISMA conexión física.
        var segunda = await LeerAsync(fabrica, new SesionSql(s.B1, s.FirmaB, s.CB1), token);
        Assert.Equal(s.B1, segunda.UuidUsuarios);
        Assert.Equal(s.FirmaB, segunda.UuidFirmas);
        Assert.Equal(s.CB1, segunda.UuidEmpresas);
        AssertEsLaMismaConexion(primera, segunda, "firma B después de firma A");

        // 3. Alcance 3: sin sesión. Las tres llaves en Guid.Empty, la misma conexión física y 0 filas.
        var tercera = await LeerAsync(fabrica, SesionSql.SinSesion, token);
        Assert.Equal(Guid.Empty, tercera.UuidUsuarios);
        Assert.Equal(Guid.Empty, tercera.UuidFirmas);
        Assert.Equal(Guid.Empty, tercera.UuidEmpresas);
        AssertEsLaMismaConexion(primera, tercera, "sin sesión después de firma B");
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            Assert.Empty(await contexto.Database.SqlQueryRaw<Guid>("SELECT UUID_EMPRESAS AS [Value] FROM T_EMPRESAS").ToListAsync(token));
        }

        // Control del paso anterior: la misma conexión, con la sesión de A, sí ve su cliente.
        fabrica.Sesion.Actuar(s.A1, s.FirmaA);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            Assert.Equal([s.CA1], await contexto.Database.SqlQueryRaw<Guid>("SELECT UUID_EMPRESAS AS [Value] FROM T_EMPRESAS").ToListAsync(token));
        }

        // 4. Lo mismo con consultas síncronas (ToList), que abren la conexión por ConnectionOpened y no por
        // ConnectionOpenedAsync.
        var sincrona1 = LeerSincrono(fabrica, new SesionSql(s.B1, s.FirmaB, s.CB1));
        Assert.Equal(s.B1, sincrona1.UuidUsuarios);
        Assert.Equal(s.FirmaB, sincrona1.UuidFirmas);
        Assert.Equal(s.CB1, sincrona1.UuidEmpresas);
        AssertEsLaMismaConexion(primera, sincrona1, "firma B (síncrona) después de sin sesión");

        var sincrona2 = LeerSincrono(fabrica, new SesionSql(s.A2, s.FirmaA, s.CA2));
        Assert.Equal(s.A2, sincrona2.UuidUsuarios);
        Assert.Equal(s.FirmaA, sincrona2.UuidFirmas);
        Assert.Equal(s.CA2, sincrona2.UuidEmpresas);
        AssertEsLaMismaConexion(primera, sincrona2, "firma A (síncrona) después de firma B");

        // Y una sin cliente activo: la llave de empresas queda en Guid.Empty, no en el CB1/CA2 de antes.
        var sincrona3 = LeerSincrono(fabrica, new SesionSql(s.A1, s.FirmaA));
        Assert.Equal(Guid.Empty, sincrona3.UuidEmpresas);
        AssertEsLaMismaConexion(primera, sincrona3, "sin cliente activo (síncrona)");
    }

    private static async Task<FilaDeSesion> LeerAsync(FabricaNitido fabrica, SesionSql sesion, CancellationToken token)
    {
        fabrica.Sesion.Actuar(sesion);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
        try
        {
            return await LecturaDeSesionSql.LeerAsync(contexto, token);
        }
        catch (Exception ex) when (ex.GetBaseException() is SqlException { Number: ErrorDeLlaveDeSoloLectura })
        {
            throw new Xunit.Sdk.XunitException(
                "La conexión reutilizada del pool conservó el SESSION_CONTEXT de solo lectura de la petición anterior "
                + "(error 15664): el contexto se arrastra entre firmas.");
        }
    }

    private static FilaDeSesion LeerSincrono(FabricaNitido fabrica, SesionSql sesion)
    {
        fabrica.Sesion.Actuar(sesion);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
        try
        {
            return LecturaDeSesionSql.Leer(contexto);
        }
        catch (Exception ex) when (ex.GetBaseException() is SqlException { Number: ErrorDeLlaveDeSoloLectura })
        {
            throw new Xunit.Sdk.XunitException(
                "La conexión reutilizada del pool conservó el SESSION_CONTEXT de solo lectura de la petición anterior "
                + "(error 15664, camino síncrono): el contexto se arrastra entre firmas.");
        }
    }

    /// <summary>
    /// Mismo <c>@@SPID</c> y mismo <c>connect_time</c>: la misma conexión física. Sin esto la prueba pasaría en
    /// vacío si el pool entregara conexiones nuevas, que nacen limpias.
    /// </summary>
    private static void AssertEsLaMismaConexion(FilaDeSesion referencia, FilaDeSesion otra, string cuando)
    {
        Assert.True(
            referencia.Spid == otra.Spid && referencia.ConnectTime == otra.ConnectTime,
            $"({cuando}) no es la misma conexión física: SPID {referencia.Spid} contra {otra.Spid}, connect_time {referencia.ConnectTime:O} contra {otra.ConnectTime:O}. "
            + "La prueba no estaría probando la reutilización del pool.");
    }
}
