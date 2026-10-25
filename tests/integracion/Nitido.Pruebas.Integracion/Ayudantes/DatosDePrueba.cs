using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Datos de prueba con llaves y documentos únicos, para no depender de que la base esté vacía.
/// Con la seguridad por fila de F0-02b, toda escritura va con la sesión de quien la crea: estos ayudantes
/// fijan <c>fabrica.Sesion.Actuar(usuario, firma)</c> antes de guardar y la dejan así al terminar (los
/// BLOCK de SQL Server rechazan una fila de otra firma o un acceso de otro usuario).
/// </summary>
internal static class DatosDePrueba
{
    public static string CorreoUnico() => $"{Guid.NewGuid():N}@prueba.nitido.co";

    /// <summary>Número de documento de 9 dígitos, distinto en cada llamada.</summary>
    public static string NitUnico() => Random.Shared.Next(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Un usuario con la llave vacía: la pone el interceptor de auditoría al guardar.</summary>
    public static Usuario NuevoUsuario(Guid uuidFirmas, string rol = "ADMINISTRADOR", string? correo = null)
    {
        var direccion = correo ?? CorreoUnico();
        return new Usuario
        {
            UuidFirmas = uuidFirmas,
            Nombre = "Contadora de prueba",
            Correo = direccion,
            CorreoNormalizado = direccion.ToUpperInvariant(),
            Rol = rol,
            Activo = true,
        };
    }

    /// <summary>
    /// Un usuario con la llave ya asignada (GUID versión 7 con la hora dada). Hace falta cuando la sesión debe
    /// conocer al usuario antes de guardarlo: el predicado de <c>T_USUARIOS_EMPRESAS</c> compara el usuario del
    /// acceso con el de la sesión.
    /// </summary>
    public static Usuario NuevoUsuarioConLlave(Guid uuidFirmas, DateTimeOffset ahora, string rol = "ADMINISTRADOR", string? correo = null)
    {
        var usuario = NuevoUsuario(uuidFirmas, rol, correo);
        usuario.Uuid = Guid.CreateVersion7(ahora);
        return usuario;
    }

    /// <summary>
    /// Guarda una firma (por su fábrica de dominio) y un usuario con la llave asignada al crearlo. Antes de
    /// guardar fija la sesión en (ese usuario, esa firma) y la deja así.
    /// </summary>
    public static async Task<(Firma Firma, Usuario Usuario)> GuardarFirmaYUsuarioAsync(
        FabricaNitido fabrica,
        CancellationToken token)
    {
        var ahora = fabrica.Reloj.GetUtcNow();
        var firma = Firma.Crear($"Firma {Guid.NewGuid():N}", ahora);
        var usuario = NuevoUsuarioConLlave(firma.Uuid, ahora);
        fabrica.Sesion.Actuar(usuario.Uuid, firma.Uuid);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(firma);
        contexto.Add(usuario);
        await contexto.SaveChangesAsync(token);
        return (firma, usuario);
    }

    /// <summary>
    /// Guarda otro usuario de la misma firma (la fila de <c>T_USUARIOS</c> solo exige la firma de la sesión).
    /// Deja la sesión en (el usuario nuevo, la firma).
    /// </summary>
    public static async Task<Usuario> GuardarOtroUsuarioAsync(FabricaNitido fabrica, Guid uuidFirmas, CancellationToken token)
    {
        var usuario = NuevoUsuarioConLlave(uuidFirmas, fabrica.Reloj.GetUtcNow());
        fabrica.Sesion.Actuar(usuario.Uuid, uuidFirmas);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        contexto.Add(usuario);
        await contexto.SaveChangesAsync(token);
        return usuario;
    }

    public static Cliente NuevoCliente(FabricaNitido fabrica, Guid uuidFirmas, Guid uuidUsuario, string? nit = null) =>
        Cliente.Crear(uuidFirmas, "31", nit ?? NitUnico(), "7", "Ferretería El Tornillo S.A.S.", uuidUsuario, fabrica.Reloj.GetUtcNow());

    /// <summary>
    /// Un acceso nuevo con la llave que le pone la fábrica del dominio. <c>AccesoCliente.Crear</c> es
    /// <c>internal</c> y el dominio de Clientes no tiene <c>InternalsVisibleTo</c> para estas pruebas, así que
    /// se llama por reflexión (no se toca el código de producción). Desde F0-02b su firma es
    /// <c>Crear(Guid uuidFirmas, Guid uuidUsuarios, DateTimeOffset ahora)</c>.
    /// </summary>
    public static AccesoCliente NuevoAccesoDeLaFabrica(Guid uuidFirmas, Guid uuidUsuarios, DateTimeOffset ahora)
    {
        var fabricaDeDominio = typeof(AccesoCliente).GetMethod(
            "Crear",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(Guid), typeof(Guid), typeof(DateTimeOffset)])
            ?? throw new InvalidOperationException("AccesoCliente.Crear(Guid, Guid, DateTimeOffset) no existe; actualizar este ayudante.");
        return (AccesoCliente)fabricaDeDominio.Invoke(null, [uuidFirmas, uuidUsuarios, ahora])!;
    }

    /// <summary>
    /// Agrega un acceso a un cliente ya cargado. El dominio todavía no tiene un método para esto (el primer
    /// acceso lo crea <c>Cliente.Crear</c>), así que se usa el campo privado de la colección.
    /// </summary>
    public static void AgregarAcceso(Cliente cliente, AccesoCliente acceso)
    {
        var campo = typeof(Cliente).GetField("_accesos", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Cliente._accesos ya no existe; actualizar este ayudante.");
        ((List<AccesoCliente>)campo.GetValue(cliente)!).Add(acceso);
    }

    /// <summary>
    /// Guarda un cliente nuevo. Antes fija la sesión en (el usuario de su primer acceso, la firma del cliente):
    /// es la única sesión con la que los BLOCK dejan pasar a la vez la fila de <c>T_EMPRESAS</c> (firma) y la de
    /// <c>T_USUARIOS_EMPRESAS</c> (firma y usuario). Deja la sesión así.
    /// </summary>
    public static async Task GuardarAsync(FabricaNitido fabrica, Cliente cliente, CancellationToken token)
    {
        fabrica.Sesion.Actuar(cliente.Accesos[0].UuidUsuarios, cliente.UuidFirmas);

        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
        contexto.Add(cliente);
        await contexto.SaveChangesAsync(token);
    }

    /// <summary>
    /// Crea un usuario nuevo de la firma y le da acceso a un cliente que ya existe. Un usuario no puede darle
    /// acceso a otro (el BLOCK de <c>T_USUARIOS_EMPRESAS</c> exige que el acceso sea del usuario de la sesión),
    /// así que el cliente lo carga <paramref name="usuarioConAcceso"/> y el acceso se guarda con la sesión del
    /// usuario nuevo. Deja la sesión en (el usuario nuevo, la firma).
    /// </summary>
    public static async Task<(Usuario Usuario, AccesoCliente Acceso)> DarAccesoAUnUsuarioNuevoAsync(
        FabricaNitido fabrica,
        Guid uuidFirmas,
        Guid uuidCliente,
        Guid usuarioConAcceso,
        CancellationToken token)
    {
        var nuevo = await GuardarOtroUsuarioAsync(fabrica, uuidFirmas, token);

        fabrica.Sesion.Actuar(usuarioConAcceso, uuidFirmas);
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<ClientesDbContext>();
        var cargado = await contexto.Clientes.Include(c => c.Accesos).SingleAsync(c => c.Uuid == uuidCliente, token);

        fabrica.Sesion.Actuar(nuevo.Uuid, uuidFirmas);
        var acceso = NuevoAccesoDeLaFabrica(uuidFirmas, nuevo.Uuid, fabrica.Reloj.GetUtcNow());
        AgregarAcceso(cargado, acceso);
        await contexto.SaveChangesAsync(token);
        return (nuevo, acceso);
    }
}
