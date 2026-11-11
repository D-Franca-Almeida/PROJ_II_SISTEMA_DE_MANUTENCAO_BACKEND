using Microsoft.AspNetCore.Http;

namespace CPTM.Manutencao.Api.Services
{
    public class UploadsOptions
    {
        public string Raiz { get; set; } = string.Empty;
    }

    public interface IFotoStorage
    {
        Task<string> SalvarAsync(IFormFile arquivo, string extensao);

        void Excluir(string url);
    }

    public class FotoStorage : IFotoStorage
    {
        public const string RequestPath = "/uploads";
        private const string Subpasta = "ssl";
        private readonly string _pasta;

        public FotoStorage(UploadsOptions opcoes)
        {
            _pasta = Path.Combine(opcoes.Raiz, Subpasta);
            Directory.CreateDirectory(_pasta);
        }

        public async Task<string> SalvarAsync(IFormFile arquivo, string extensao)
        {
            var nome = $"{Guid.NewGuid():N}{extensao}";
            var caminho = Path.Combine(_pasta, nome);
            try
            {
                await using var destino = new FileStream(caminho, FileMode.CreateNew);
                await arquivo.CopyToAsync(destino);
            }
            catch
            {
                ExcluirArquivo(caminho);
                throw;
            }
            return $"{RequestPath}/{Subpasta}/{nome}";
        }

        public void Excluir(string url)
        {
            ExcluirArquivo(Path.Combine(_pasta, Path.GetFileName(url)));
        }

        private static void ExcluirArquivo(string caminho)
        {
            try { File.Delete(caminho); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
