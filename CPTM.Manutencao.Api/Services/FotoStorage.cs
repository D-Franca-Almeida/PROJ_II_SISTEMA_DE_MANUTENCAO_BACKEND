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
            await using (var destino = new FileStream(Path.Combine(_pasta, nome), FileMode.CreateNew))
                await arquivo.CopyToAsync(destino);
            return $"{RequestPath}/{Subpasta}/{nome}";
        }

        public void Excluir(string url)
        {
            var caminho = Path.Combine(_pasta, Path.GetFileName(url));
            try { File.Delete(caminho); } catch (IOException) { }
        }
    }
}
