using Nitido.Compartido.Multiempresa;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Contexto de sesión solo para pruebas: hasta que F0-03a traiga el ingreso, ninguna petición tiene sesión
/// real. Se registra únicamente desde <see cref="FabricaNitido"/> (y <c>ContextoDePrueba_NoSeRegistraEnLaApi</c>
/// comprueba que el host no lo registra). Arranca como una petición sin sesión: todo en <see cref="Guid.Empty"/>
/// y sin cliente activo. Cada prueba dice quién actúa con <see cref="Actuar"/>.
/// </summary>
public sealed class ContextoSesionDePrueba : IContextoSesion
{
    public Guid UuidUsuarios { get; set; } = Guid.Empty;

    public Guid UuidFirmas { get; set; } = Guid.Empty;

    public Guid? UuidEmpresas { get; set; }

    /// <summary>Fija de una vez el usuario, la firma y (opcional) el cliente activo de las siguientes operaciones.</summary>
    public void Actuar(Guid uuidUsuarios, Guid uuidFirmas, Guid? uuidEmpresas = null)
    {
        UuidUsuarios = uuidUsuarios;
        UuidFirmas = uuidFirmas;
        UuidEmpresas = uuidEmpresas;
    }

    /// <summary>Lo mismo que <see cref="Actuar"/>, a partir de una sesión descrita para SQL directo.</summary>
    public void Actuar(SesionSql sesion) => Actuar(sesion.UuidUsuarios, sesion.UuidFirmas, sesion.UuidEmpresas);

    /// <summary>La sesión actual, para leer con SQL directo con la misma identidad.</summary>
    public SesionSql ComoSql() => new(UuidUsuarios, UuidFirmas, UuidEmpresas);
}
