using Nitido.Clientes.Dominio.Clientes;

namespace Nitido.Pruebas.Unidad.Dominio;

/// <summary>
/// Pruebas de <see cref="Cliente.Crear"/> y de <c>AccesoCliente.Crear</c>. Esta última es
/// <c>internal</c> y el dominio de Clientes no tiene <c>InternalsVisibleTo</c> para las pruebas, así
/// que se cubre por el único camino real de uso: el primer acceso que crea el cliente.
/// </summary>
public class ClientePruebas
{
    private static readonly DateTimeOffset Ahora = new(2031, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);
    private static readonly Guid UuidFirmas = Guid.CreateVersion7(Ahora);
    private static readonly Guid UuidUsuario = Guid.CreateVersion7(Ahora.AddDays(-1));

    private static Cliente NuevoCliente(DateTimeOffset? ahora = null, Guid? usuario = null) =>
        Cliente.Crear(UuidFirmas, "31", "900123456", "7", "Ferretería El Tornillo S.A.S.", usuario ?? UuidUsuario, ahora ?? Ahora);

    [Fact]
    public void Crear_CopiaLosDatosYQuedaActivo()
    {
        var cliente = NuevoCliente();

        Assert.Equal(UuidFirmas, cliente.UuidFirmas);
        Assert.Equal("31", cliente.TipoDocumento);
        Assert.Equal("900123456", cliente.NumeroDocumento);
        Assert.Equal("7", cliente.DigitoVerificacion);
        Assert.Equal("Ferretería El Tornillo S.A.S.", cliente.RazonSocial);
        Assert.True(cliente.Activo);
    }

    [Fact]
    public void Crear_AsignaLlaveVersion7ConElMomentoRecibido()
    {
        var cliente = NuevoCliente();

        GuidVersion7Ayuda.AssertEsVersion7Con(cliente.Uuid, Ahora);
    }

    [Fact]
    public void Crear_ConMomentoEnOtraZonaHoraria_UsaElInstanteUtc()
    {
        var enBogota = new DateTimeOffset(2031, 3, 4, 0, 6, 7, 89, TimeSpan.FromHours(-5));

        var cliente = NuevoCliente(enBogota);

        Assert.Equal(Ahora.ToUnixTimeMilliseconds(), cliente.Uuid.MarcaDeTiempoEnMilisegundos());
        Assert.Equal(Ahora.ToUnixTimeMilliseconds(), cliente.Accesos.Single().Uuid.MarcaDeTiempoEnMilisegundos());
    }

    [Fact]
    public void Crear_DaUnPrimerAccesoAlUsuarioIndicado()
    {
        var cliente = NuevoCliente();

        var acceso = Assert.Single(cliente.Accesos);
        Assert.Equal(UuidUsuario, acceso.UuidUsuarios);
    }

    [Fact]
    public void Crear_ElAccesoTieneSuPropiaLlaveVersion7ConElMismoMomento()
    {
        var cliente = NuevoCliente();

        var acceso = Assert.Single(cliente.Accesos);
        GuidVersion7Ayuda.AssertEsVersion7Con(acceso.Uuid, Ahora);
        Assert.NotEqual(cliente.Uuid, acceso.Uuid);
    }

    [Fact]
    public void Crear_DosClientesIguales_NoComparteLlavesNiAccesos()
    {
        var uno = NuevoCliente();
        var otro = NuevoCliente();

        Assert.NotEqual(uno.Uuid, otro.Uuid);
        Assert.NotEqual(uno.Accesos.Single().Uuid, otro.Accesos.Single().Uuid);
        Assert.NotSame(uno.Accesos, otro.Accesos);
    }

    [Fact]
    public void Crear_ElAccesoLlevaLaFirmaDelCliente()
    {
        // F0-02b, decisión 1 del ADR 0003: AccesoCliente.Crear(uuidFirmas, uuidUsuarios, ahora) la llena desde
        // Cliente.Crear. La llave foránea compuesta (UUID_EMPRESAS, UUID_FIRMAS) y la política de
        // T_USUARIOS_EMPRESAS dependen de que sea la misma firma del cliente.
        var cliente = NuevoCliente();

        var acceso = Assert.Single(cliente.Accesos);
        Assert.Equal(UuidFirmas, acceso.UuidFirmas);
        Assert.Equal(cliente.UuidFirmas, acceso.UuidFirmas);

        var deOtraFirma = Cliente.Crear(Guid.CreateVersion7(Ahora.AddDays(-3)), "31", "900123456", "7", "Otra firma", UuidUsuario, Ahora);
        Assert.NotEqual(UuidFirmas, deOtraFirma.UuidFirmas);
        Assert.Equal(deOtraFirma.UuidFirmas, Assert.Single(deOtraFirma.Accesos).UuidFirmas);
    }

    [Fact]
    public void Crear_ConOtroUsuario_GuardaEseUsuarioEnElAcceso()
    {
        var otroUsuario = Guid.CreateVersion7(Ahora.AddDays(-2));

        var cliente = NuevoCliente(usuario: otroUsuario);

        Assert.Equal(otroUsuario, cliente.Accesos.Single().UuidUsuarios);
    }
}
