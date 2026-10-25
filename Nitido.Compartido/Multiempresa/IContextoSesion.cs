namespace Nitido.Compartido.Multiempresa;

/// <summary>
/// El contexto de la petición en curso: la única interfaz que dice quién está actuando, de qué firma y sobre qué
/// cliente. La usan el interceptor de auditoría (quién creó o modificó cada fila), el filtro global de EF y el
/// interceptor que fija el <c>SESSION_CONTEXT</c> de SQL Server (ADR 0003). <see cref="Guid.Empty"/> significa
/// «sin sesión» en todo el sistema.
/// </summary>
public interface IContextoSesion
{
    /// <summary>Usuario que actúa. <see cref="Guid.Empty"/> si la petición no tiene sesión.</summary>
    Guid UuidUsuarios { get; }

    /// <summary>Firma del usuario. <see cref="Guid.Empty"/> si no hay sesión.</summary>
    Guid UuidFirmas { get; }

    /// <summary>
    /// Cliente activo: el <c>{uuidCliente}</c> de la ruta, ya validado por el filtro de endpoint. <c>null</c> si
    /// la ruta no lleva cliente o si todavía no se ha validado.
    /// </summary>
    Guid? UuidEmpresas { get; }
}
