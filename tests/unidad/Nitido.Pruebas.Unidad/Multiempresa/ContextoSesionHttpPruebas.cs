using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nitido.Compartido.Api.Multiempresa;

namespace Nitido.Pruebas.Unidad.Multiempresa;

/// <summary>
/// <c>ContextoSesionHttp</c> es el <c>IContextoSesion</c> del host (F0-02b, 1.3): el usuario y la firma salen de
/// los reclamos de <c>HttpContext.User</c>; el cliente activo, solo de <c>HttpContext.Items</c>, donde lo deja el
/// filtro de endpoint cuando validó el acceso. Nunca lee el <c>{uuidCliente}</c> de la ruta por su cuenta y lee en
/// cada acceso, sin guardar valores. Cualquier cosa rara (sin petición, sin reclamo, un valor que no es GUID) da
/// <see cref="Guid.Empty"/>: sin sesión, el sistema falla cerrado.
/// </summary>
public class ContextoSesionHttpPruebas
{
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly Guid Firma = Guid.CreateVersion7();
    private static readonly Guid Cliente = Guid.CreateVersion7();

    private static DefaultHttpContext Peticion(params Claim[] reclamos)
    {
        var contexto = new DefaultHttpContext();
        if (reclamos.Length > 0)
        {
            contexto.User = new ClaimsPrincipal(new ClaimsIdentity(reclamos, "prueba"));
        }

        return contexto;
    }

    private static Claim DeUsuario(string valor) => new(ReclamosDeSesion.UuidUsuarios, valor);

    private static Claim DeFirma(string valor) => new(ReclamosDeSesion.UuidFirmas, valor);

    private static ContextoSesionHttp Sesion(HttpContext? contexto) =>
        new(new HttpContextAccessor { HttpContext = contexto });

    [Fact]
    public void ContextoSesionHttp_SoloEntregaElClienteValidado()
    {
        // Sin reclamos: sin sesión.
        var sinReclamos = Sesion(Peticion());
        Assert.Equal(Guid.Empty, sinReclamos.UuidUsuarios);
        Assert.Equal(Guid.Empty, sinReclamos.UuidFirmas);
        Assert.Null(sinReclamos.UuidEmpresas);

        // Con reclamos válidos: el usuario y la firma.
        var conReclamos = Sesion(Peticion(DeUsuario(Usuario.ToString()), DeFirma(Firma.ToString())));
        Assert.Equal(Usuario, conReclamos.UuidUsuarios);
        Assert.Equal(Firma, conReclamos.UuidFirmas);

        // Un reclamo que no es GUID da Guid.Empty.
        var noEsGuid = Sesion(Peticion(DeUsuario("no-es-un-guid"), DeFirma("12345")));
        Assert.Equal(Guid.Empty, noEsGuid.UuidUsuarios);
        Assert.Equal(Guid.Empty, noEsGuid.UuidFirmas);

        // Un {uuidCliente} en la ruta, sin la marca del filtro: UuidEmpresas = null.
        var sinMarca = Peticion(DeUsuario(Usuario.ToString()), DeFirma(Firma.ToString()));
        sinMarca.Request.RouteValues["uuidCliente"] = Cliente.ToString();
        Assert.Null(Sesion(sinMarca).UuidEmpresas);

        // Con la marca del filtro: el cliente.
        var conMarca = Peticion(DeUsuario(Usuario.ToString()), DeFirma(Firma.ToString()));
        conMarca.Request.RouteValues["uuidCliente"] = Cliente.ToString();
        conMarca.Items[ClienteActivo.ClaveValidada] = Cliente;
        Assert.Equal(Cliente, Sesion(conMarca).UuidEmpresas);
    }

    [Fact]
    public void ContextoSesionHttp_SinPeticionNoHaySesion()
    {
        // Fuera de una petición (un trabajo en segundo plano, el arranque, las pruebas de EF) no hay HttpContext.
        var sesion = Sesion(null);

        Assert.Equal(Guid.Empty, sesion.UuidUsuarios);
        Assert.Equal(Guid.Empty, sesion.UuidFirmas);
        Assert.Null(sesion.UuidEmpresas);
    }

    [Fact]
    public void ContextoSesionHttp_UnSoloReclamoDejaElOtroVacio()
    {
        var soloUsuario = Sesion(Peticion(DeUsuario(Usuario.ToString())));
        Assert.Equal(Usuario, soloUsuario.UuidUsuarios);
        Assert.Equal(Guid.Empty, soloUsuario.UuidFirmas);

        var soloFirma = Sesion(Peticion(DeFirma(Firma.ToString())));
        Assert.Equal(Guid.Empty, soloFirma.UuidUsuarios);
        Assert.Equal(Firma, soloFirma.UuidFirmas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{no-es-guid}")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ContextoSesionHttp_UnReclamoInvalidoOVacioDaGuidEmpty(string valor)
    {
        var sesion = Sesion(Peticion(DeUsuario(valor), DeFirma(valor)));

        Assert.Equal(Guid.Empty, sesion.UuidUsuarios);
        Assert.Equal(Guid.Empty, sesion.UuidFirmas);
    }

    [Fact]
    public void ContextoSesionHttp_NuncaLeeElClienteDeLaRutaPorSuCuenta()
    {
        // La ruta dice un cliente y el filtro validó otro: manda lo que validó el filtro, nunca la ruta.
        var otroCliente = Guid.CreateVersion7();
        var peticion = Peticion(DeUsuario(Usuario.ToString()), DeFirma(Firma.ToString()));
        peticion.Request.RouteValues["uuidCliente"] = otroCliente.ToString();
        peticion.Items[ClienteActivo.ClaveValidada] = Cliente;

        Assert.Equal(Cliente, Sesion(peticion).UuidEmpresas);
    }

    [Fact]
    public void ContextoSesionHttp_UnaMarcaQueNoEsUnGuidNoDaCliente()
    {
        // Solo el filtro pone la marca, y pone un Guid. Cualquier otra cosa en ese lugar no puede convertirse en
        // un cliente activo (falla cerrado, sin lanzar).
        var peticion = Peticion(DeUsuario(Usuario.ToString()), DeFirma(Firma.ToString()));
        peticion.Items[ClienteActivo.ClaveValidada] = Cliente.ToString();

        Assert.Null(Sesion(peticion).UuidEmpresas);
    }

    [Fact]
    public void ContextoSesionHttp_LeeEnCadaAccesoSinGuardarValores()
    {
        // Se registra por alcance, pero no debe congelar nada: si la petición cambia (el filtro pone la marca
        // después de que se creó el contexto, o cambian los reclamos), lo siguiente que se lea lo refleja.
        var peticion = Peticion(DeUsuario(Usuario.ToString()), DeFirma(Firma.ToString()));
        var sesion = Sesion(peticion);
        Assert.Null(sesion.UuidEmpresas);
        Assert.Equal(Usuario, sesion.UuidUsuarios);

        peticion.Items[ClienteActivo.ClaveValidada] = Cliente;
        Assert.Equal(Cliente, sesion.UuidEmpresas);

        var otroUsuario = Guid.CreateVersion7();
        var otraFirma = Guid.CreateVersion7();
        peticion.User = new ClaimsPrincipal(new ClaimsIdentity([DeUsuario(otroUsuario.ToString()), DeFirma(otraFirma.ToString())], "prueba"));
        Assert.Equal(otroUsuario, sesion.UuidUsuarios);
        Assert.Equal(otraFirma, sesion.UuidFirmas);

        peticion.Items.Remove(ClienteActivo.ClaveValidada);
        Assert.Null(sesion.UuidEmpresas);
    }

    [Fact]
    public void ContextoSesionHttp_SigueAlHttpContextDelAccessorEnCadaAcceso()
    {
        // IHttpContextAccessor entrega el HttpContext de la petición en curso: si cambia, el contexto lo sigue.
        var accessor = new HttpContextAccessor { HttpContext = Peticion(DeUsuario(Usuario.ToString()), DeFirma(Firma.ToString())) };
        var sesion = new ContextoSesionHttp(accessor);
        Assert.Equal(Firma, sesion.UuidFirmas);

        var otraFirma = Guid.CreateVersion7();
        accessor.HttpContext = Peticion(DeUsuario(Usuario.ToString()), DeFirma(otraFirma.ToString()));
        Assert.Equal(otraFirma, sesion.UuidFirmas);

        accessor.HttpContext = null;
        Assert.Equal(Guid.Empty, sesion.UuidFirmas);
    }

    [Fact]
    public void ReclamosDeSesion_TienenLosNombresDelDiseno()
    {
        // Decisión propuesta en el diseño (1.3); F0-03a los emite al ingresar. Si cambian, cambia la cookie.
        Assert.Equal("uuid_usuarios", ReclamosDeSesion.UuidUsuarios);
        Assert.Equal("uuid_firmas", ReclamosDeSesion.UuidFirmas);
    }
}
