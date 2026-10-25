using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Compartido.Infraestructura;
using Nitido.Firmas.Aplicacion.Autenticacion;
using Nitido.Firmas.Aplicacion.Registro;
using Nitido.Firmas.Infraestructura.Autenticacion;
using Nitido.Firmas.Infraestructura.Persistencia;
using Nitido.Firmas.Infraestructura.Registro;

namespace Nitido.Firmas.Infraestructura;

public static class ModuloFirmas
{
    /// <summary>
    /// Registra los servicios del módulo Firmas: su <c>DbContext</c>, la autenticación, el registro de firmas y la
    /// siembra de Development. Requiere que el host haya llamado antes a <c>AgregarPersistenciaNitido</c> y que
    /// registre Data Protection (cifra el secreto del doble factor). Los valores de <c>Seguridad:*</c> se leen de la
    /// configuración cuando se usan, no aquí, para que las pruebas puedan cambiarlos.
    /// </summary>
    public static IServiceCollection AgregarFirmas(this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddDbContext<FirmasDbContext>((proveedor, opciones) => opciones.UsarSqlServerNitido(proveedor, "Firmas"));

        servicios.AddOptions<OpcionesDeSeguridadDeFirmas>()
            .Configure<IConfiguration>((opciones, config) =>
            {
                opciones.IntentosMaximos = OpcionesDeSeguridadDeFirmas.LeerEntero(
                    config, "Seguridad:Bloqueo:IntentosMaximos", OpcionesDeSeguridadDeFirmas.IntentosMaximosPorDefecto);
                opciones.MinutosDeBloqueo = OpcionesDeSeguridadDeFirmas.LeerEntero(
                    config, "Seguridad:Bloqueo:Minutos", OpcionesDeSeguridadDeFirmas.MinutosDeBloqueoPorDefecto);
            });
        servicios.AddSingleton(proveedor => new PoliticaDeClave(OpcionesDeSeguridadDeFirmas.LeerEntero(
            proveedor.GetRequiredService<IConfiguration>(),
            "Seguridad:Clave:LargoMinimo",
            OpcionesDeSeguridadDeFirmas.LargoMinimoDeClavePorDefecto)));
        servicios.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();

        servicios.AddScoped<IServicioDeAutenticacion, ServicioDeAutenticacion>();
        servicios.AddScoped<IRegistroDeFirmas, RegistroDeFirmas>();
        servicios.AddScoped<RegistrarFirmaCasoDeUso>();
        servicios.AddHostedService<SemillaDeDesarrollo>();
        return servicios;
    }
}
