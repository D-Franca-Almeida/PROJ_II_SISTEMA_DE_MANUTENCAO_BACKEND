namespace CPTM.Manutencao.Tests;

public sealed class RelogioFalso : TimeProvider
{
    private DateTimeOffset _agora;

    public RelogioFalso(DateTime utc) => _agora = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    public void Avancar(TimeSpan intervalo) => _agora += intervalo;

    public override DateTimeOffset GetUtcNow() => _agora;
}
