using FluentValidation;
using Nitido.Clientes.Dominio.Clientes;
using Nitido.Compartido.Multiempresa;
using Nitido.Compartido.Resultados;

namespace Nitido.Clientes.Aplicacion.Clientes;

/// <summary>Lo que ve el frontend de un cliente.</summary>
public sealed record ClienteDto(
    Guid Uuid,
    string TipoDocumento,
    string NumeroDocumento,
    string DigitoVerificacion,
    string RazonSocial,
    bool Activo);

public sealed record CrearClienteComando(string RazonSocial, string NumeroDocumento);

/// <summary>Puerto de escritura: guarda un cliente nuevo con su primer acceso. Lo implementa Infraestructura.</summary>
public interface IRepositorioDeClientes
{
    /// <summary>Guarda el cliente. Devuelve <c>false</c> si la firma ya tiene un cliente con ese documento.</summary>
    Task<bool> AgregarAsync(Cliente cliente, CancellationToken cancelacion);
}

/// <summary>Puerto de lectura de clientes. Solo devuelve los de la firma y a los que el usuario de la sesión tiene acceso.</summary>
public interface IConsultasDeClientes
{
    Task<IReadOnlyList<ClienteDto>> ListarAsync(CancellationToken cancelacion);

    Task<ClienteDto?> ObtenerAsync(Guid uuid, CancellationToken cancelacion);
}

public sealed class CrearClienteValidador : AbstractValidator<CrearClienteComando>
{
    public CrearClienteValidador()
    {
        RuleFor(c => c.RazonSocial)
            .Cascade(CascadeMode.Stop)
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Escribe la razón social o el nombre del cliente.")
            .MaximumLength(200).WithMessage("La razón social puede tener hasta 200 caracteres.");

        RuleFor(c => c.NumeroDocumento)
            .Must(d => !string.IsNullOrWhiteSpace(d)).WithMessage("Escribe el NIT o la cédula del cliente.");
    }
}

/// <summary>Crea un cliente en la firma de la sesión. Quien lo crea queda con acceso a él.</summary>
public sealed class CrearClienteCasoDeUso(IRepositorioDeClientes repositorio, IContextoSesion sesion, TimeProvider reloj)
{
    public async Task<Resultado<ClienteDto>> EjecutarAsync(CrearClienteComando comando, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var validacion = await new CrearClienteValidador().ValidateAsync(comando, cancelacion);
        if (!validacion.IsValid)
        {
            return validacion.ComoError();
        }

        var nit = Nit.Crear(comando.NumeroDocumento);
        if (nit.EsError)
        {
            return nit.Error;
        }

        // La firma y el usuario salen siempre de la sesión, nunca de lo que llegue en el cuerpo.
        if (sesion.UuidFirmas == Guid.Empty || sesion.UuidUsuarios == Guid.Empty)
        {
            return Error.NoAutorizado("sin_sesion", "Necesitas ingresar para continuar.");
        }

        var cliente = Cliente.Crear(
            sesion.UuidFirmas,
            Nit.TipoDocumento,
            nit.Valor.Numero,
            nit.Valor.DigitoVerificacion,
            comando.RazonSocial.Trim(),
            sesion.UuidUsuarios,
            reloj.GetUtcNow());

        if (!await repositorio.AgregarAsync(cliente, cancelacion))
        {
            return Error.Conflicto("cliente_repetido", "Este cliente ya está en tu firma.");
        }

        return new ClienteDto(
            cliente.Uuid,
            cliente.TipoDocumento,
            cliente.NumeroDocumento,
            cliente.DigitoVerificacion,
            cliente.RazonSocial,
            cliente.Activo);
    }
}

public sealed class ListarClientesCasoDeUso(IConsultasDeClientes consultas)
{
    public Task<IReadOnlyList<ClienteDto>> EjecutarAsync(CancellationToken cancelacion) => consultas.ListarAsync(cancelacion);
}

public sealed class ObtenerClienteCasoDeUso(IConsultasDeClientes consultas)
{
    public async Task<Resultado<ClienteDto>> EjecutarAsync(Guid uuid, CancellationToken cancelacion)
    {
        var cliente = await consultas.ObtenerAsync(uuid, cancelacion);
        return cliente is null
            ? Error.NoEncontrado("cliente_no_encontrado", "No encontramos ese cliente.")
            : cliente;
    }
}
