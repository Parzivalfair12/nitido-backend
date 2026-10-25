namespace Nitido.Firmas.Dominio.Firmas;

/// <summary>La firma de contadores: el inquilino que agrupa a sus usuarios y a sus clientes.</summary>
public sealed class Firma
{
    private Firma()
    {
    }

    public Guid Uuid { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Crea una firma con una llave GUID versión 7 tomada del momento recibido.</summary>
    public static Firma Crear(string nombre, DateTimeOffset ahora) =>
        new()
        {
            Uuid = Guid.CreateVersion7(ahora),
            Nombre = nombre,
        };
}
