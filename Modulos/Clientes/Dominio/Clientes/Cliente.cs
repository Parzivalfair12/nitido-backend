using Nitido.Compartido.Multiempresa;

namespace Nitido.Clientes.Dominio.Clientes;

/// <summary>
/// El cliente de la firma (la empresa cuya contabilidad se lleva). F0-04 reemplaza los tres textos
/// del documento por el objeto de valor <c>Nit</c>.
/// </summary>
public sealed class Cliente : IDeFirma
{
    private readonly List<AccesoCliente> _accesos = [];

    private Cliente()
    {
    }

    public Guid Uuid { get; private set; }

    public Guid UuidFirmas { get; private set; }

    /// <summary>Código de la DIAN del tipo de documento (31 = NIT).</summary>
    public string TipoDocumento { get; private set; } = string.Empty;

    public string NumeroDocumento { get; private set; } = string.Empty;

    public string DigitoVerificacion { get; private set; } = string.Empty;

    public string RazonSocial { get; private set; } = string.Empty;

    public bool Activo { get; private set; }

    public IReadOnlyList<AccesoCliente> Accesos => _accesos;

    /// <summary>Crea el cliente con una llave GUID versión 7 y le da su primer acceso a un usuario.</summary>
    public static Cliente Crear(
        Guid uuidFirmas,
        string tipoDocumento,
        string numeroDocumento,
        string digitoVerificacion,
        string razonSocial,
        Guid uuidUsuariosConAcceso,
        DateTimeOffset ahora)
    {
        var cliente = new Cliente
        {
            Uuid = Guid.CreateVersion7(ahora),
            UuidFirmas = uuidFirmas,
            TipoDocumento = tipoDocumento,
            NumeroDocumento = numeroDocumento,
            DigitoVerificacion = digitoVerificacion,
            RazonSocial = razonSocial,
            Activo = true,
        };
        cliente._accesos.Add(AccesoCliente.Crear(uuidFirmas, uuidUsuariosConAcceso, ahora));
        return cliente;
    }
}
