namespace CPTM.Manutencao.Api.Models
{
    // Corpo da requisição de devolução: o motivo é obrigatório
    public class DevolucaoDto
    {
        public string Motivo { get; set; } = string.Empty;
    }
}
