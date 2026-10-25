namespace Nitido.Compartido.Api.Multiempresa;

/// <summary>
/// Clave de <c>HttpContext.Items</c> donde el filtro del cliente activo deja el cliente que ya validó. Solo el
/// filtro la escribe; <c>ContextoSesionHttp</c> la lee.
/// </summary>
internal static class ClienteActivo
{
    public const string ClaveValidada = "Nitido.ClienteActivoValidado";
}
