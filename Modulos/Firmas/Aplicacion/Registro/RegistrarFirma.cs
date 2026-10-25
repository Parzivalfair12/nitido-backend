using FluentValidation;
using Nitido.Compartido.Resultados;
using Nitido.Firmas.Aplicacion.Validacion;

namespace Nitido.Firmas.Aplicacion.Registro;

public sealed record AdministradorNuevo(string Nombre, string Correo, string Clave);

public sealed record RegistrarFirmaComando(string NombreFirma, AdministradorNuevo Administrador);

/// <summary>Política de contraseña (F0-03a): solo largo mínimo, sin reglas de composición. Sale de la configuración.</summary>
public sealed record PoliticaDeClave(int LargoMinimo);

/// <summary>
/// Puerto del registro: crea la firma y su administrador en una sola transacción. Lo implementa Infraestructura.
/// Responde <c>Conflicto</c> si el correo ya existe.
/// </summary>
public interface IRegistroDeFirmas
{
    Task<Resultado<Guid>> RegistrarAsync(
        string nombreFirma,
        string nombreAdministrador,
        string correoAdministrador,
        string claveAdministrador,
        CancellationToken cancelacion);
}

public sealed class RegistrarFirmaValidador : AbstractValidator<RegistrarFirmaComando>
{
    public RegistrarFirmaValidador(PoliticaDeClave politica)
    {
        RuleFor(c => c.NombreFirma)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Escribe el nombre de la firma.")
            .MaximumLength(200).WithMessage("El nombre de la firma puede tener hasta 200 caracteres.");

        RuleFor(c => c.Administrador)
            .NotNull().WithMessage("Escribe los datos del administrador.")
            .SetValidator(new AdministradorValidador(politica)!);
    }

    private sealed class AdministradorValidador : AbstractValidator<AdministradorNuevo>
    {
        public AdministradorValidador(PoliticaDeClave politica)
        {
            RuleFor(a => a.Nombre)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Escribe tu nombre.")
                .MaximumLength(200).WithMessage("El nombre puede tener hasta 200 caracteres.");

            RuleFor(a => a.Correo)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Escribe tu correo.")
                .MaximumLength(256).WithMessage("El correo puede tener hasta 256 caracteres.")
                .EmailAddress().WithMessage("Escribe un correo válido.");

            RuleFor(a => a.Clave)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage($"La contraseña debe tener al menos {politica.LargoMinimo} caracteres.")
                .MinimumLength(politica.LargoMinimo).WithMessage($"La contraseña debe tener al menos {politica.LargoMinimo} caracteres.")
                .MaximumLength(128).WithMessage("La contraseña puede tener hasta 128 caracteres.");
        }
    }
}

/// <summary>Caso de uso: registrar una firma de contadores con su administrador.</summary>
public sealed class RegistrarFirmaCasoDeUso(IRegistroDeFirmas registro, PoliticaDeClave politica)
{
    public async Task<Resultado<Guid>> EjecutarAsync(RegistrarFirmaComando comando, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var validacion = await new RegistrarFirmaValidador(politica).ValidateAsync(comando, cancelacion);
        if (!validacion.IsValid)
        {
            return validacion.ComoError();
        }

        var admin = comando.Administrador;
        return await registro.RegistrarAsync(
            comando.NombreFirma.Trim(),
            admin.Nombre.Trim(),
            admin.Correo.Trim(),
            admin.Clave,
            cancelacion);
    }
}
