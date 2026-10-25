using Microsoft.AspNetCore.Http;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Compartido.Api.Multiempresa;

/// <summary>
/// <see cref="IContextoSesion"/> del host. El usuario y la firma salen de los reclamos de
/// <c>HttpContext.User</c>; el cliente activo, solo de <c>HttpContext.Items</c>, donde lo deja el filtro de
/// endpoint cuando validó el acceso (nunca se lee el valor de la ruta por su cuenta). Lee en cada acceso, sin
/// guardar valores. Sin petición, sin reclamo o con un valor que no es GUID da <see cref="Guid.Empty"/> (sin
/// sesión): el sistema falla cerrado.
/// </summary>
internal sealed class ContextoSesionHttp(IHttpContextAccessor accesor, ContextoDeAlcance? alcance = null) : IContextoSesion
{
    public Guid UuidUsuarios => alcance is { Fijado: true } ? alcance.UuidUsuarios : LeerReclamo(ReclamosDeSesion.UuidUsuarios);

    public Guid UuidFirmas => alcance is { Fijado: true } ? alcance.UuidFirmas : LeerReclamo(ReclamosDeSesion.UuidFirmas);

    public Guid? UuidEmpresas =>
        alcance is { Fijado: true }
            ? alcance.UuidEmpresas
            : accesor.HttpContext?.Items.TryGetValue(ClienteActivo.ClaveValidada, out var valor) == true && valor is Guid cliente
                ? cliente
                : null;

    private Guid LeerReclamo(string nombre)
    {
        var valor = accesor.HttpContext?.User.FindFirst(nombre)?.Value;
        return Guid.TryParse(valor, out var guid) ? guid : Guid.Empty;
    }
}
