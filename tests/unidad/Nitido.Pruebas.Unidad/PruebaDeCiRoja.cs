namespace Nitido.Pruebas.Unidad;

public class PruebaDeCiRoja
{
    [Fact]
    public void Falla_A_Proposito_Para_Comprobar_Que_El_Ci_Frena_El_Merge() =>
        Assert.Fail("Prueba intencional de F0-06 etapa 2: el CI debe ponerse en rojo y bloquear el merge.");
}
