namespace CPTM.Manutencao.Api.Models
{
    public record EstacaoDto(int Id, string Nome, string Linha);

    public record EquipamentoDto(int Id, int EstacaoId, string Descricao, string Patrimonio);
}
