using System.Reflection;

namespace Nitido.Clientes.Aplicacion;

/// <summary>
/// Existe solo para que el ensamblado de Nitido.Clientes.Aplicacion no quede casi vacío mientras el módulo no
/// tiene casos de uso: Smart App Control de Windows puede bloquear una .dll sin contenido (error 0x800711C7;
/// ver la sección «Si Windows bloquea un archivo» del README). Sus miembros no los usa ningún código; se
/// dejaron porque con una clase vacía el bloqueo volvió. Se borra cuando este proyecto tenga sus primeras
/// clases de verdad.
/// </summary>
internal sealed class MarcadorDeEnsamblado
{
    /// <summary>Ensamblado que contiene esta marca.</summary>
    internal static Assembly Ensamblado => typeof(MarcadorDeEnsamblado).Assembly;

    /// <summary>Nombre del módulo al que pertenece este ensamblado.</summary>
    internal static string NombreDelModulo => Ensamblado.GetName().Name!.Split('.')[1];
}
