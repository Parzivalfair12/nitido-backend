using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nitido.Compartido.Infraestructura;
using Nitido.Compartido.Infraestructura.Persistencia;
using Nitido.Firmas.Aplicacion.Registro;
using Nitido.Firmas.Infraestructura.Autenticacion;
using Nitido.Firmas.Infraestructura.Persistencia;

namespace Nitido.Firmas.Infraestructura.Registro;

/// <summary>
/// Datos para probar a mano (F0-03a): al arrancar en Development crea una firma y su administrador con el correo y la
/// clave de los user-secrets <c>Desarrollo:UsuarioInicial:Correo</c> y <c>Desarrollo:UsuarioInicial:Clave</c>. Fuera de
/// Development no hace nada. Si la base todavía no tiene las migraciones aplicadas, avisa y deja arrancar la API.
/// </summary>
internal sealed class SemillaDeDesarrollo(
    IHostEnvironment entorno,
    IConfiguration configuracion,
    IServiceScopeFactory alcances,
    ILogger<SemillaDeDesarrollo> registro) : IHostedService
{
    public const string NombreDeLaFirma = "Firma de desarrollo";
    public const string NombreDelAdministrador = "Administrador";

    public async Task StartAsync(CancellationToken cancelacion)
    {
        if (!entorno.IsDevelopment())
        {
            return;
        }

        var correo = configuracion["Desarrollo:UsuarioInicial:Correo"];
        var clave = configuracion["Desarrollo:UsuarioInicial:Clave"];
        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(clave))
        {
            return;
        }

        try
        {
            if (await ExisteAsync(correo, cancelacion))
            {
                return;
            }

            using var alcance = alcances.CreateScope();
            var casoDeUso = alcance.ServiceProvider.GetRequiredService<RegistrarFirmaCasoDeUso>();
            var resultado = await casoDeUso.EjecutarAsync(
                new RegistrarFirmaComando(NombreDeLaFirma, new AdministradorNuevo(NombreDelAdministrador, correo, clave)),
                cancelacion);
            if (resultado.EsError)
            {
                registro.LogWarning("No se pudo crear el usuario inicial de desarrollo: {Mensaje}", resultado.Error.Mensaje);
            }
            else
            {
                registro.LogInformation("Se creó la firma y el usuario inicial de desarrollo.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogWarning(ex, "No se pudo sembrar el usuario inicial de desarrollo. ¿Aplicaste las migraciones?");
        }
    }

    public Task StopAsync(CancellationToken cancelacion) => Task.CompletedTask;

    private async Task<bool> ExisteAsync(string correo, CancellationToken cancelacion)
    {
        var normalizado = ServicioDeAutenticacion.Normalizar(correo);
        using var alcance = alcances.CrearAlcanceParaIngreso(normalizado);
        var baseDeDatos = alcance.ServiceProvider.GetRequiredService<FirmasDbContext>();
        return await baseDeDatos.Usuarios
            .IgnoreQueryFilters([DbContextNitido.FiltroMultiempresa])
            .AnyAsync(u => u.CorreoNormalizado == normalizado, cancelacion);
    }
}
