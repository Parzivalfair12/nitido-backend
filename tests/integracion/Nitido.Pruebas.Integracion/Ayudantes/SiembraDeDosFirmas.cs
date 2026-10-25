using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Las dos firmas de las pruebas de aislamiento (F0-02b, 8.2). Llaves y NIT únicos en cada llamada: la base
/// compartida tiene filas de otras pruebas y cada prueba cuenta solo las suyas.
/// <list type="table">
/// <item><term>Firma A</term><description>usuarios A1 y A2 (administradores); cliente CA1 con acceso solo de A1;
/// cliente CA2 con acceso <b>solo</b> de A2.</description></item>
/// <item><term>Firma B</term><description>usuario B1; cliente CB1 con acceso de B1.</description></item>
/// </list>
/// Cada fila se guarda con la sesión de quien la crea. Después se modifica una vez cada firma, cada usuario, cada
/// cliente y cada acceso, para que <b>los ocho historiales tengan filas</b> y las pruebas sobre
/// <c>_HISTORIAL</c> no pasen en vacío. (La especificación pide cambiar CA1, CB1, A1 y B1; aquí se cambian además
/// CA2, A2 y las dos firmas y se toca cada acceso, porque sin historial de CA2 la prueba «el usuario sin
/// permiso no ve el cliente en el historial» pasaría en vacío.)
/// </summary>
internal sealed record SiembraDeDosFirmas(
    Guid FirmaA,
    Guid FirmaB,
    Guid A1,
    Guid A2,
    Guid B1,
    Guid CA1,
    Guid CA2,
    Guid CB1,
    Guid AccesoA1,
    Guid AccesoA2,
    Guid AccesoB1)
{
    public SesionSql SesionA1 => new(A1, FirmaA);

    public SesionSql SesionA2 => new(A2, FirmaA);

    public SesionSql SesionB1 => new(B1, FirmaB);

    public static async Task<SiembraDeDosFirmas> CrearAsync(FabricaNitido fabrica, string cadena, CancellationToken token)
    {
        var ahora = fabrica.Reloj.GetUtcNow();
        var firmaA = Firma.Crear($"Firma A {Guid.NewGuid():N}", ahora);
        var firmaB = Firma.Crear($"Firma B {Guid.NewGuid():N}", ahora);
        var a1 = DatosDePrueba.NuevoUsuarioConLlave(firmaA.Uuid, ahora);
        var a2 = DatosDePrueba.NuevoUsuarioConLlave(firmaA.Uuid, ahora);
        var b1 = DatosDePrueba.NuevoUsuarioConLlave(firmaB.Uuid, ahora);

        // Firmas y usuarios: cada fila con la sesión de su firma.
        await GuardarAsync<FirmasDbContext>(fabrica, new SesionSql(a1.Uuid, firmaA.Uuid), token, firmaA, a1);
        await GuardarAsync<FirmasDbContext>(fabrica, new SesionSql(a2.Uuid, firmaA.Uuid), token, a2);
        await GuardarAsync<FirmasDbContext>(fabrica, new SesionSql(b1.Uuid, firmaB.Uuid), token, firmaB, b1);

        // Clientes: cada uno con la sesión de quien recibe el acceso.
        var ca1 = Cliente.Crear(firmaA.Uuid, "31", DatosDePrueba.NitUnico(), "7", "Cliente CA1", a1.Uuid, ahora);
        var ca2 = Cliente.Crear(firmaA.Uuid, "31", DatosDePrueba.NitUnico(), "7", "Cliente CA2", a2.Uuid, ahora);
        var cb1 = Cliente.Crear(firmaB.Uuid, "31", DatosDePrueba.NitUnico(), "7", "Cliente CB1", b1.Uuid, ahora);
        await GuardarAsync<ClientesDbContext>(fabrica, new SesionSql(a1.Uuid, firmaA.Uuid), token, ca1);
        await GuardarAsync<ClientesDbContext>(fabrica, new SesionSql(a2.Uuid, firmaA.Uuid), token, ca2);
        await GuardarAsync<ClientesDbContext>(fabrica, new SesionSql(b1.Uuid, firmaB.Uuid), token, cb1);

        var siembra = new SiembraDeDosFirmas(
            firmaA.Uuid,
            firmaB.Uuid,
            a1.Uuid,
            a2.Uuid,
            b1.Uuid,
            ca1.Uuid,
            ca2.Uuid,
            cb1.Uuid,
            ca1.Accesos[0].Uuid,
            ca2.Accesos[0].Uuid,
            cb1.Accesos[0].Uuid);

        await siembra.ModificarUnaVezAsync(fabrica, cadena, token);
        fabrica.Sesion.Actuar(SesionSql.SinSesion);
        return siembra;
    }

    private static async Task GuardarAsync<TContexto>(
        FabricaNitido fabrica,
        SesionSql sesion,
        CancellationToken token,
        params object[] entidades)
        where TContexto : DbContext
    {
        fabrica.Sesion.Actuar(sesion);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<TContexto>();
        contexto.AddRange(entidades);
        await contexto.SaveChangesAsync(token);
    }

    /// <summary>Un cambio por fila para que cada tabla temporal escriba su versión anterior en el historial.</summary>
    private async Task ModificarUnaVezAsync(FabricaNitido fabrica, string cadena, CancellationToken token)
    {
        // Que la creación y el cambio no caigan en el mismo milisegundo: con periodos de duración cero SQL Server
        // no guarda la versión anterior.
        await Task.Delay(50, token);

        foreach (var (sesion, uuidFirmas, uuidUsuario) in new[] { (SesionA1, FirmaA, A1), (SesionA2, FirmaA, A2), (SesionB1, FirmaB, B1) })
        {
            fabrica.Sesion.Actuar(sesion);
            using var alcance = fabrica.Services.CreateScope();
            var firmas = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();

            // La firma (una vez por firma: la cambia su primer usuario) y el propio usuario.
            if (uuidUsuario != A2)
            {
                var firma = await firmas.Firmas.SingleAsync(f => f.Uuid == uuidFirmas, token);
                firmas.Entry(firma).Property(nameof(Firma.Nombre)).CurrentValue = $"{firma.Nombre} (cambiada)";
            }

            var usuario = await firmas.Set<Usuario>().SingleAsync(u => u.Uuid == uuidUsuario, token);
            usuario.Nombre = "Nombre cambiado";
            await firmas.SaveChangesAsync(token);
        }

        foreach (var (sesion, cliente) in new[] { (SesionA1, CA1), (SesionA2, CA2), (SesionB1, CB1) })
        {
            fabrica.Sesion.Actuar(sesion);
            using var alcance = fabrica.Services.CreateScope();
            var clientes = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
            var existente = await clientes.Clientes.SingleAsync(c => c.Uuid == cliente, token);
            clientes.Entry(existente).Property(nameof(Cliente.RazonSocial)).CurrentValue = $"{existente.RazonSocial} (cambiada)";
            await clientes.SaveChangesAsync(token);

            // El acceso no tiene campos propios que cambiar: se toca su auditoría con SQL directo y la sesión
            // de su dueño, que es la única que pasa los BLOCK de T_USUARIOS_EMPRESAS.
            var tocados = await ConsultasSql.EjecutarAsync(
                cadena,
                sesion,
                "UPDATE T_USUARIOS_EMPRESAS SET FECHA_MODIFICACION = SYSUTCDATETIME(), UUID_USUARIOS_MODIFICACION = @usuario "
                + "WHERE UUID_EMPRESAS = @cliente AND UUID_USUARIOS = @usuario",
                token,
                ("@usuario", sesion.UuidUsuarios),
                ("@cliente", cliente));
            if (tocados != 1)
            {
                throw new InvalidOperationException($"La siembra no pudo tocar el acceso del cliente {cliente}: {tocados} filas.");
            }
        }
    }
}
