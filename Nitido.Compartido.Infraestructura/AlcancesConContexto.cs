using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Compartido.Infraestructura;

/// <summary>Alcances de inyección con un contexto de sesión declarado (ADR 0003).</summary>
public static class AlcancesConContexto
{
    /// <summary>
    /// Usuario «sistema»: lo registran las columnas de auditoría cuando la operación no la hace una persona con
    /// sesión (registro de una firma, siembra de Development).
    /// </summary>
    public static readonly Guid UsuarioSistema = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Crea un alcance cuyos <c>DbContext</c> actúan con el usuario, la firma y (opcional) el cliente dados, como si
    /// vinieran de una sesión. Lo usan el registro de firma (la firma nueva y el usuario sistema) y la siembra de
    /// Development, y el ingreso para escribir los contadores de intentos del usuario ya encontrado. El llamador
    /// debe liberar el alcance.
    /// </summary>
    public static IServiceScope CrearAlcanceConContexto(
        this IServiceScopeFactory fabrica,
        Guid uuidUsuarios,
        Guid uuidFirmas,
        Guid? uuidEmpresas = null)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        var alcance = fabrica.CreateScope();
        alcance.ServiceProvider.GetRequiredService<ContextoDeAlcance>().Fijar(uuidUsuarios, uuidFirmas, uuidEmpresas);
        return alcance;
    }

    /// <summary>
    /// Crea el alcance de la búsqueda de un usuario por su correo al ingresar: sin usuario ni firma (todavía no se
    /// conocen) y con la llave <c>CORREO_INGRESO</c>, con la que la política de <c>T_USUARIOS</c> deja ver solo la
    /// fila de ese correo. El llamador debe liberar el alcance.
    /// </summary>
    public static IServiceScope CrearAlcanceParaIngreso(this IServiceScopeFactory fabrica, string correoNormalizado)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentException.ThrowIfNullOrWhiteSpace(correoNormalizado);

        var alcance = fabrica.CreateScope();
        alcance.ServiceProvider.GetRequiredService<ContextoDeAlcance>()
            .Fijar(Guid.Empty, Guid.Empty, null, correoNormalizado);
        return alcance;
    }
}
