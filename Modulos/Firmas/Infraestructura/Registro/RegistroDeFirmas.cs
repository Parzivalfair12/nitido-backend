using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Infraestructura;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Compartido.Resultados;
using Nitido.Firmas.Aplicacion.Registro;
using Nitido.Firmas.Dominio.Firmas;
using Nitido.Firmas.Infraestructura.Autenticacion;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Firmas.Infraestructura.Registro;

/// <summary>
/// Crea una firma y su administrador en una sola transacción (un solo <c>SaveChanges</c>). Corre en un alcance con
/// contexto declarado: la firma nueva y el usuario «sistema» (ADR 0003), porque todavía no hay nadie con sesión.
/// </summary>
internal sealed class RegistroDeFirmas(
    IServiceScopeFactory alcances,
    TimeProvider reloj,
    IPasswordHasher<Usuario> hasheador) : IRegistroDeFirmas
{
    public const string RolAdministrador = "ADMINISTRADOR";

    public async Task<Resultado<Guid>> RegistrarAsync(
        string nombreFirma,
        string nombreAdministrador,
        string correoAdministrador,
        string claveAdministrador,
        CancellationToken cancelacion)
    {
        var ahora = reloj.GetUtcNow();
        var firma = Firma.Crear(nombreFirma, ahora);
        var administrador = new Usuario
        {
            Uuid = Guid.CreateVersion7(ahora),
            UuidFirmas = firma.Uuid,
            Nombre = nombreAdministrador,
            Correo = correoAdministrador,
            CorreoNormalizado = ServicioDeAutenticacion.Normalizar(correoAdministrador),
            Rol = RolAdministrador,
            Activo = true,
            SelloSeguridad = Guid.NewGuid(),
        };
        administrador.ClaveHash = hasheador.HashPassword(administrador, claveAdministrador);

        using var alcance = alcances.CrearAlcanceConContexto(AlcancesConContexto.UsuarioSistema, firma.Uuid);
        var baseDeDatos = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        baseDeDatos.Firmas.Add(firma);
        baseDeDatos.Usuarios.Add(administrador);
        try
        {
            await baseDeDatos.SaveChangesAsync(cancelacion);
        }
        catch (DbUpdateException ex) when (ViolacionDeUnicidad.Es(ex))
        {
            // El índice único del correo es global: se ve aunque la política oculte los usuarios de otras firmas.
            return Error.Conflicto("correo_ya_existe", "Ya existe una cuenta con ese correo.");
        }

        return firma.Uuid;
    }
}
