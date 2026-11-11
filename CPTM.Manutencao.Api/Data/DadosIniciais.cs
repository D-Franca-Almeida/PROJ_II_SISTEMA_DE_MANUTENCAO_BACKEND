using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Massa do roteiro de testes (Testes/Roteiro-Sprints-1-2-3.ps1), antes mantida no repositório mock.
    public static class DadosIniciais
    {
        public static Solicitacao[] Solicitacoes() => new[]
        {
            // IDs 1, 2 e 3: SOLICITADA, pertencentes ao usuário Comum (comum123)
            Nova(1, "Lâmpada queimada na plataforma 2", StatusSolicitacao.Solicitada),
            Nova(2, "Escada rolante com ruído", StatusSolicitacao.Solicitada),
            Nova(3, "Catraca travada", StatusSolicitacao.Solicitada),
            // ID 4: SOLICITADA de OUTRO dono (teste de bloqueio: Comum não cancela chamado alheio)
            Nova(4, "Vazamento no banheiro", StatusSolicitacao.Solicitada, "outro456"),

            Nova(5, "Porta do vagão desalinhada", StatusSolicitacao.Cancelada),
            Nova(6, "Aparelho de ar-condicionado com defeito", StatusSolicitacao.Solicitada),
            Nova(7, "Grade de proteção solta", StatusSolicitacao.Solicitada),
            Nova(8, "Ventilador do túnel sem funcionar", StatusSolicitacao.Aprovada),
            Nova(9, "Sinalização apagada (devolvida)", StatusSolicitacao.Devolvida),
            Nova(10, "Bebedouro sem pressão", StatusSolicitacao.EmManutencao),
            Nova(11, "Piso solto na estação", StatusSolicitacao.Concluida),
            Nova(12, "Escada rolante com ruído (corrigida)", StatusSolicitacao.Reenviada)
        };

        private static Solicitacao Nova(int id, string descricao, string status, string matriculaSolicitante = "comum123") =>
            new()
            {
                Id = id,
                Descricao = descricao,
                Status = status,
                MatriculaSolicitante = matriculaSolicitante
            };
    }
}
