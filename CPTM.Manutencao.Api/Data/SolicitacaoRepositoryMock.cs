using System.Collections.Concurrent;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // MOCK em memória (registrar como Singleton para manter o estado entre requisições).
    // Na Sprint 2 será substituído por uma implementação com Oracle.
    public class SolicitacaoRepositoryMock : ISolicitacaoRepository
    {
        private readonly ConcurrentDictionary<int, Solicitacao> _banco = new();

        public SolicitacaoRepositoryMock()
        {
            // --- Massa do roteiro de testes (Sprints 1, 2 e 3) ---
            // IDs 1, 2 e 3: SOLICITADA, pertencentes ao usuário Comum (comum123)
            Semear(1, "Lâmpada queimada na plataforma 2", StatusSolicitacao.Solicitada);
            Semear(2, "Escada rolante com ruído", StatusSolicitacao.Solicitada);
            Semear(3, "Catraca travada", StatusSolicitacao.Solicitada);
            // ID 4: SOLICITADA de OUTRO dono (teste de bloqueio: Comum não cancela chamado alheio)
            Semear(4, "Vazamento no banheiro", StatusSolicitacao.Solicitada, "outro456");

            // --- Massa adicional (outros status) ---
            Semear(5, "Porta do vagão desalinhada", StatusSolicitacao.Cancelada);
            Semear(6, "Aparelho de ar-condicionado com defeito", StatusSolicitacao.Solicitada);
            Semear(7, "Grade de proteção solta", StatusSolicitacao.Solicitada);
            Semear(8, "Ventilador do túnel sem funcionar", StatusSolicitacao.Aprovada);
            Semear(9, "Sinalização apagada (devolvida)", StatusSolicitacao.Devolvida);
            Semear(10, "Bebedouro sem pressão", StatusSolicitacao.EmManutencao);
            Semear(11, "Piso solto na estação", StatusSolicitacao.Concluida);
            Semear(12, "Escada rolante com ruído (corrigida)", StatusSolicitacao.Reenviada);
        }

        public Solicitacao? ObterPorId(int id) =>
            _banco.TryGetValue(id, out var solicitacao) ? solicitacao : null;

        public void Salvar(Solicitacao solicitacao) => _banco[solicitacao.Id] = solicitacao;

        // Por padrão, as solicitações semeadas pertencem ao usuário comum do mock (comum123)
        private void Semear(int id, string descricao, string status, string matriculaSolicitante = "comum123") =>
            _banco[id] = new Solicitacao
            {
                Id = id,
                Descricao = descricao,
                Status = status,
                MatriculaSolicitante = matriculaSolicitante
            };
    }
}
