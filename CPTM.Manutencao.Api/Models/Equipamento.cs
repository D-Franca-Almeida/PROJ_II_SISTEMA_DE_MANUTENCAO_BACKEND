namespace CPTM.Manutencao.Api.Models
{
    public class Equipamento
    {
        public int Id { get; set; }
        public int EstacaoId { get; set; }
        public Estacao? Estacao { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public string Patrimonio { get; set; } = string.Empty;
    }
}
