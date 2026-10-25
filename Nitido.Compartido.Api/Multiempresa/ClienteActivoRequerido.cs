namespace Nitido.Compartido.Api.Multiempresa;

/// <summary>
/// Marca de metadatos que <see cref="RutasDelCliente.MapearRutasDelCliente"/> pone en toda ruta de cliente. Una
/// prueba recorre los endpoints y falla si una ruta con <c>{uuidCliente}</c> no la lleva.
/// </summary>
public sealed class ClienteActivoRequerido;
