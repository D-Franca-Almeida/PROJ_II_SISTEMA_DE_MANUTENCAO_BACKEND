using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    public interface INotificacaoRepository
    {
        // Só rastreia: a notificação é gravada pelo próximo SaveChanges do contexto, junto com a transição que a gerou
        void Adicionar(Notificacao notificacao);

        // Da matrícula ou do perfil, mais novas primeiro (Data desc, Id desc)
        List<Notificacao> ListarDoUsuario(string matricula, string perfil, bool apenasNaoLidas);

        int ContarNaoLidas(string matricula, string perfil);

        Notificacao? ObterPorId(int id);

        void Salvar();
    }
}
