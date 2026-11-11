using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CPTM.Manutencao.Api.Models
{
    public class CriarSolicitacaoRequest
    {
        [FromForm(Name = "equipamentoId")]
        public int? EquipamentoId { get; set; }

        [FromForm(Name = "descricao")]
        public string? Descricao { get; set; }

        [FromForm(Name = "foto")]
        public IFormFile? Foto { get; set; }
    }

    public record EquipamentoDetalheDto(int Id, string Descricao, string Patrimonio, int EstacaoId, string EstacaoNome, string Linha);

    public record SolicitacaoCriadaDto(
        int Id,
        string Status,
        string Descricao,
        string FotoUrl,
        DateTime DataAbertura,
        string MatriculaSolicitante,
        EquipamentoDto Equipamento,
        EstacaoDto Estacao);
}
