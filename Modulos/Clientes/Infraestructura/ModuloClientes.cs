using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nitido.Clientes.Aplicacion.Clientes;
using Nitido.Clientes.Infraestructura.Persistencia;
using Nitido.Compartido.Infraestructura;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Clientes.Infraestructura;

public static class ModuloClientes
{
    /// <summary>
    /// Registra los servicios del módulo Clientes: su <c>DbContext</c>, el puerto <see cref="IAccesoAClientes"/> y los
    /// casos de uso con sus puertos. Requiere que el host haya llamado antes a <c>AgregarPersistenciaNitido</c>.
    /// </summary>
    public static IServiceCollection AgregarClientes(this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddDbContext<ClientesDbContext>((proveedor, opciones) => opciones.UsarSqlServerNitido(proveedor, "Clientes"));
        servicios.AddScoped<IAccesoAClientes, AccesoAClientes>();

        servicios.AddScoped<IRepositorioDeClientes, RepositorioDeClientes>();
        servicios.AddScoped<IConsultasDeClientes, ConsultasDeClientes>();
        servicios.AddScoped<CrearClienteCasoDeUso>();
        servicios.AddScoped<ListarClientesCasoDeUso>();
        servicios.AddScoped<ObtenerClienteCasoDeUso>();
        return servicios;
    }
}
