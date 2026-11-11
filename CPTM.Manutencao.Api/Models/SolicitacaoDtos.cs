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

    public class ReenviarSolicitacaoRequest
    {
        [FromForm(Name = "descricao")]
        public string? Descricao { get; set; }

        [FromForm(Name = "foto")]
        public IFormFile? Foto { get; set; }
    }

    public record EquipamentoDetalheDto(int Id, string Descricao, string Patrimonio, int EstacaoId, string EstacaoNome, string Linha);

    public record SolicitacaoDto(
        int Id,
        string Status,
        string Descricao,
        string? FotoUrl,
        DateTime DataAbertura,
        string MatriculaSolicitante,
        EquipamentoDto? Equipamento,
        EstacaoDto? Estacao,
        string? MotivoDevolucao = null,
        int? DiasParaExpirar = null,
        string? NumeroSsm = null,
        DateTime? DataRecebimento = null);

    public record HistoricoStatusDto(string? StatusAnterior, string StatusNovo, string Matricula, DateTime Data, string? Observacao);

    public record SolicitacaoDetalheDto(
        int Id,
        string Status,
        string Descricao,
        string? FotoUrl,
        DateTime DataAbertura,
        string MatriculaSolicitante,
        EquipamentoDto? Equipamento,
        EstacaoDto? Estacao,
        string? MotivoDevolucao,
        string? MatriculaTecnico,
        DateTime? DataInicioManutencao,
        List<HistoricoStatusDto> Historico,
        int? DiasParaExpirar = null,
        string? NumeroSsm = null,
        DateTime? DataRecebimento = null,
        int? OrdemServicoId = null);
}
