namespace Nitido.Pruebas.Integracion;

/// <summary>Reloj controlado: solo fija «ahora». Los temporizadores siguen siendo los del sistema.</summary>
public sealed class RelojDePrueba(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;

    public override DateTimeOffset GetUtcNow() => Ahora;
}
