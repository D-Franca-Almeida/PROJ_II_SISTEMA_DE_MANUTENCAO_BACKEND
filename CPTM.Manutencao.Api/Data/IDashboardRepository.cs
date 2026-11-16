namespace CPTM.Manutencao.Api.Data
{
    // Consultas agregadas do dashboard (US-27), feitas no banco
    public interface IDashboardRepository
    {
        // Quantidade de SSLs por status atual; só traz os status que têm SSL
        Dictionary<string, int> ContarPorStatus();

        // Horas entre a primeira entrada da SSL em "de" e a primeira em "para" (HistoricoStatus), uma por SSL que passou pelos dois nessa ordem
        List<double> DuracoesEmHorasEntre(string de, string para);
    }
}
