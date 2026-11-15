namespace CPTM.Manutencao.Api.Features.Retirada.Commands
{
    // US-24: nome e documento (CPF ou matrícula) do terceiro que vai retirar, ambos obrigatórios
    public class AutorizarRetiradaRequest
    {
        public string? Nome { get; set; }
        public string? Documento { get; set; }
    }
}
