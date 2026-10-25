namespace Nitido.Compartido.Multiempresa;

/// <summary>
/// Contexto de sesión declarado de forma explícita para un alcance de inyección de dependencias (ADR 0003):
/// el registro de una firma, la siembra de Development y la búsqueda del usuario por su correo al ingresar corren
/// en un alcance propio, no en una petición con sesión. Es un servicio por alcance: cada alcance tiene el suyo.
/// Mientras no se llame a <see cref="Fijar"/>, <see cref="Fijado"/> es <c>false</c> y el contexto de sesión del host
/// sigue saliendo de la petición.
/// </summary>
public sealed class ContextoDeAlcance
{
    /// <summary>Indica si este alcance trae un contexto declarado que reemplaza al de la petición.</summary>
    public bool Fijado { get; private set; }

    public Guid UuidUsuarios { get; private set; }

    public Guid UuidFirmas { get; private set; }

    public Guid? UuidEmpresas { get; private set; }

    /// <summary>
    /// Correo normalizado que se está buscando al ingresar. Solo lo usa la búsqueda del usuario por correo, antes de
    /// saber su firma; viaja a SQL Server en la llave de sesión <c>CORREO_INGRESO</c>. Vacío en cualquier otro caso.
    /// </summary>
    public string CorreoDeIngreso { get; private set; } = string.Empty;

    /// <summary>
    /// Declara el contexto de este alcance. Solo lo llaman los alcances de <c>Nitido.Compartido.Infraestructura</c>
    /// (<c>CrearAlcanceConContexto</c> y <c>CrearAlcanceParaIngreso</c>), una vez por alcance: ningún otro servicio
    /// puede suplantar al usuario, la firma o la llave de ingreso.
    /// </summary>
    internal void Fijar(Guid uuidUsuarios, Guid uuidFirmas, Guid? uuidEmpresas = null, string correoDeIngreso = "")
    {
        if (Fijado)
        {
            throw new InvalidOperationException("El contexto de este alcance ya estaba declarado.");
        }

        UuidUsuarios = uuidUsuarios;
        UuidFirmas = uuidFirmas;
        UuidEmpresas = uuidEmpresas;
        CorreoDeIngreso = correoDeIngreso;
        Fijado = true;
    }
}
