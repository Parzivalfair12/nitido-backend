using Nitido.Clientes.Dominio.Clientes;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Unidad.Multiempresa;

/// <summary>
/// Los contratos de multiempresa de <c>Nitido.Compartido</c> (F0-02b, 1.1): <see cref="IDeFirma"/>,
/// <see cref="IDeEmpresa"/>, <see cref="IContextoSesion"/> completo y el puerto <see cref="IAccesoAClientes"/>.
/// Las implementaciones de abajo compilan solo si las firmas son exactamente las del diseño; las pruebas de
/// reflexión fijan lo que el compilador no ve (que sean públicas, de solo lectura y estén donde deben).
/// </summary>
public class ContratosDeMultiempresaPruebas
{
    private sealed class DeFirmaDePrueba : IDeFirma
    {
        public Guid UuidFirmas { get; init; }
    }

    private sealed class DeEmpresaDePrueba : IDeEmpresa
    {
        public Guid UuidEmpresas { get; init; }
    }

    private sealed class SesionDePrueba : IContextoSesion
    {
        public Guid UuidUsuarios { get; init; }

        public Guid UuidFirmas { get; init; }

        public Guid? UuidEmpresas { get; init; }
    }

    private sealed class AccesoDePrueba(bool respuesta) : IAccesoAClientes
    {
        public Guid? Consultado { get; private set; }

        public Task<bool> PuedeEntrarAsync(Guid uuidEmpresas, CancellationToken ct)
        {
            Consultado = uuidEmpresas;
            return Task.FromResult(respuesta);
        }
    }

    [Fact]
    public void IContextoSesion_Trae_ElUsuarioLaFirmaYElClienteActivo()
    {
        var usuario = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var cliente = Guid.CreateVersion7();

        IContextoSesion conCliente = new SesionDePrueba { UuidUsuarios = usuario, UuidFirmas = firma, UuidEmpresas = cliente };
        IContextoSesion sinCliente = new SesionDePrueba { UuidUsuarios = usuario, UuidFirmas = firma };

        Assert.Equal((usuario, firma, (Guid?)cliente), (conCliente.UuidUsuarios, conCliente.UuidFirmas, conCliente.UuidEmpresas));
        Assert.Null(sinCliente.UuidEmpresas);
    }

    [Fact]
    public void IContextoSesion_EsLaMismaInterfazDeF002a_SinCrearOtra()
    {
        // Decisión del 2026-10-18: no hay IUsuarioActual. IContextoSesion sigue siendo la única interfaz del
        // contexto de la petición, ahora con tres propiedades de solo lectura.
        var propiedades = typeof(IContextoSesion).GetProperties().OrderBy(p => p.Name).ToList();

        Assert.Equal(["UuidEmpresas", "UuidFirmas", "UuidUsuarios"], propiedades.Select(p => p.Name));
        Assert.Equal(typeof(Guid?), propiedades.Single(p => p.Name == "UuidEmpresas").PropertyType);
        Assert.Equal(typeof(Guid), propiedades.Single(p => p.Name == "UuidFirmas").PropertyType);
        Assert.Equal(typeof(Guid), propiedades.Single(p => p.Name == "UuidUsuarios").PropertyType);
        Assert.All(propiedades, p =>
        {
            Assert.True(p.CanRead, $"{p.Name} debe poderse leer.");
            Assert.False(p.CanWrite, $"{p.Name} debe ser de solo lectura en la interfaz.");
        });
        Assert.DoesNotContain(typeof(IContextoSesion).GetMethods(), m => !m.IsSpecialName); // solo los accesores de las propiedades
        Assert.DoesNotContain(
            typeof(IContextoSesion).Assembly.GetTypes(),
            t => t.Name is "IUsuarioActual" or "IContextoDeSesion");
    }

    [Fact]
    public void IDeFirma_ExigeUuidFirmas()
    {
        IDeFirma entidad = new DeFirmaDePrueba { UuidFirmas = Guid.CreateVersion7() };

        Assert.NotEqual(Guid.Empty, entidad.UuidFirmas);
        var propiedad = Assert.Single(typeof(IDeFirma).GetProperties());
        Assert.Equal("UuidFirmas", propiedad.Name);
        Assert.Equal(typeof(Guid), propiedad.PropertyType);
    }

    [Fact]
    public void IDeEmpresa_ExigeUuidEmpresas()
    {
        IDeEmpresa entidad = new DeEmpresaDePrueba { UuidEmpresas = Guid.CreateVersion7() };

        Assert.NotEqual(Guid.Empty, entidad.UuidEmpresas);
        var propiedad = Assert.Single(typeof(IDeEmpresa).GetProperties());
        Assert.Equal("UuidEmpresas", propiedad.Name);
        Assert.Equal(typeof(Guid), propiedad.PropertyType);
    }

    [Fact]
    public async Task IAccesoAClientes_PreguntaPorUnClienteYResponde()
    {
        var cliente = Guid.CreateVersion7();
        var permite = new AccesoDePrueba(respuesta: true);
        var niega = new AccesoDePrueba(respuesta: false);

        Assert.True(await ((IAccesoAClientes)permite).PuedeEntrarAsync(cliente, TestContext.Current.CancellationToken));
        Assert.False(await ((IAccesoAClientes)niega).PuedeEntrarAsync(cliente, TestContext.Current.CancellationToken));
        Assert.Equal(cliente, permite.Consultado);

        var metodo = Assert.Single(typeof(IAccesoAClientes).GetMethods());
        Assert.Equal(nameof(IAccesoAClientes.PuedeEntrarAsync), metodo.Name);
        Assert.Equal(typeof(Task<bool>), metodo.ReturnType);
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], metodo.GetParameters().Select(p => p.ParameterType));
    }

    [Theory]
    [InlineData(typeof(IDeFirma))]
    [InlineData(typeof(IDeEmpresa))]
    [InlineData(typeof(IContextoSesion))]
    [InlineData(typeof(IAccesoAClientes))]
    public void LosContratosSonInterfacesPublicasDeNitidoCompartidoMultiempresa(Type contrato)
    {
        Assert.True(contrato.IsInterface);
        Assert.True(contrato.IsPublic, $"{contrato.Name} debe ser pública: la usan los módulos y Nitido.Compartido.Api.");
        Assert.Equal("Nitido.Compartido.Multiempresa", contrato.Namespace);
        Assert.Equal("Nitido.Compartido", contrato.Assembly.GetName().Name);
    }

    [Fact]
    public void Cliente_YAccesoCliente_SonDeUnaFirma()
    {
        // Cliente : IDeFirma (no IDeEmpresa: la lista de clientes no tiene cliente activo); AccesoCliente : IDeFirma
        // con UuidFirmas (decisión 1 del ADR 0003). Por eso el filtro global de EF los cubre.
        Assert.True(typeof(IDeFirma).IsAssignableFrom(typeof(Cliente)));
        Assert.True(typeof(IDeFirma).IsAssignableFrom(typeof(AccesoCliente)));
        Assert.False(typeof(IDeEmpresa).IsAssignableFrom(typeof(Cliente)));
        Assert.False(typeof(IDeEmpresa).IsAssignableFrom(typeof(AccesoCliente)));
    }
}
