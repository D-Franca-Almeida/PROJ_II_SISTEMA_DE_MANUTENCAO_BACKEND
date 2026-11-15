using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CPTM.Manutencao.Api.Services
{
    public class AzureStorageOptions
    {
        public string? ConnectionString { get; set; }
        public string ContainerFotos { get; set; } = "uploads";
        public string FilaEventos { get; set; } = "eventos-manutencao";

        // Login e transições esperam a fila: com o storage fora, cada chamada não pode levar os ~37 s do retry padrão
        public static void ConfigurarRetryCurto(RetryOptions retry)
        {
            retry.MaxRetries = 1;
            retry.Delay = TimeSpan.FromMilliseconds(500);
            retry.NetworkTimeout = TimeSpan.FromSeconds(5);
        }

        public static void ValidarConnectionString(string connectionString)
        {
            try
            {
                _ = new BlobServiceClient(connectionString);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new InvalidOperationException("AzureStorage:ConnectionString com formato inválido.", ex);
            }
        }
    }

    // Upload no Blob falhou (storage fora, credencial ou permissão) -> o Controller responde 503
    public class ArmazenamentoIndisponivelException : Exception
    {
        public ArmazenamentoIndisponivelException(Exception interna)
            : base("O armazenamento de fotos está indisponível no momento. Tente novamente em instantes.", interna) { }
    }

    // Representa um evento assíncrono publicado na fila de mensageria (Login ou Transição de Status da SSL)
    public record EventoSistemaMensagem(
        string TipoEvento,
        string Matricula,
        int? SolicitacaoId,
        string? StatusAnterior,
        string? StatusNovo,
        string? Detalhe,
        DateTime DataUtc);

    public interface INotificacaoQueue
    {
        void Publicar(EventoSistemaMensagem evento);
        IReadOnlyList<EventoSistemaMensagem> ObterRecentes(int limite = 50);
    }

    // Fallback em memória (usado automaticamente quando AzureStorage:ConnectionString não está configurada)
    public class InMemoryNotificacaoQueue : INotificacaoQueue
    {
        private readonly ConcurrentQueue<EventoSistemaMensagem> _fila = new();

        public void Publicar(EventoSistemaMensagem evento)
        {
            _fila.Enqueue(evento);
            while (_fila.Count > 200)
                _fila.TryDequeue(out _);
        }

        public IReadOnlyList<EventoSistemaMensagem> ObterRecentes(int limite = 50) =>
            _fila.Reverse().Take(Math.Clamp(limite, 1, 200)).ToList();
    }

    // Implementação real com Azure Queue Storage (stcptmmanut2026 / fila "eventos-manutencao")
    public class AzureQueueNotificacaoService : INotificacaoQueue
    {
        private readonly QueueClient _queueClient;
        private readonly ILogger<AzureQueueNotificacaoService> _logger;
        private readonly InMemoryNotificacaoQueue _espelhoMemoria = new();
        private volatile bool _filaCriada;

        // Com o storage fora, só a primeira requisição espera o retry; as seguintes não tentam a rede durante a pausa
        public static readonly TimeSpan PausaAposFalha = TimeSpan.FromSeconds(30);
        private DateTime _pausadaAteUtc = DateTime.MinValue;

        public AzureQueueNotificacaoService(AzureStorageOptions opcoes, ILogger<AzureQueueNotificacaoService> logger)
        {
            if (string.IsNullOrWhiteSpace(opcoes.ConnectionString))
                throw new InvalidOperationException("AzureStorage:ConnectionString não configurada.");

            var opcoesFila = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
            AzureStorageOptions.ConfigurarRetryCurto(opcoesFila.Retry);
            _queueClient = new QueueClient(opcoes.ConnectionString, opcoes.FilaEventos, opcoesFila);
            _logger = logger;
        }

        // Falha da fila nunca derruba o login nem a transição: é logada e engolida
        public void Publicar(EventoSistemaMensagem evento)
        {
            _espelhoMemoria.Publicar(evento);
            if (DateTime.UtcNow < _pausadaAteUtc)
                return;

            try
            {
                GarantirFila();
                _queueClient.SendMessage(JsonSerializer.Serialize(evento));
            }
            catch (Exception ex)
            {
                _pausadaAteUtc = DateTime.UtcNow + PausaAposFalha;
                _logger.LogWarning(ex, "Falha ao publicar o evento {TipoEvento} na fila {Fila}; novos envios pausados por {Pausa}.",
                    evento.TipoEvento, _queueClient.Name, PausaAposFalha);
            }
        }

        public IReadOnlyList<EventoSistemaMensagem> ObterRecentes(int limite = 50) =>
            _espelhoMemoria.ObterRecentes(limite);

        private void GarantirFila()
        {
            if (_filaCriada) return;
            _queueClient.CreateIfNotExists();
            _filaCriada = true;
        }
    }

    // Implementação real de Object Storage na nuvem com Azure Blob Storage (container "uploads")
    public class AzureBlobFotoStorage : IFotoStorage
    {
        private const string Subpasta = "ssl";
        private readonly BlobContainerClient _container;
        private readonly ILogger<AzureBlobFotoStorage> _logger;
        private volatile bool _containerCriado;

        public AzureBlobFotoStorage(AzureStorageOptions opcoes, ILogger<AzureBlobFotoStorage> logger)
        {
            if (string.IsNullOrWhiteSpace(opcoes.ConnectionString))
                throw new InvalidOperationException("AzureStorage:ConnectionString não configurada.");

            var opcoesBlob = new BlobClientOptions();
            AzureStorageOptions.ConfigurarRetryCurto(opcoesBlob.Retry);
            _container = new BlobContainerClient(opcoes.ConnectionString, opcoes.ContainerFotos, opcoesBlob);
            _logger = logger;
        }

        public async Task<string> SalvarAsync(IFormFile arquivo, string extensao)
        {
            var nomeBlob = $"{Subpasta}/{Guid.NewGuid():N}{extensao}";
            var blob = _container.GetBlobClient(nomeBlob);

            var contentType = extensao.Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";

            try
            {
                await GarantirContainerAsync();
                await using var stream = arquivo.OpenReadStream();
                await blob.UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
                });
            }
            catch (Exception ex) when (ex is RequestFailedException or AggregateException)
            {
                _logger.LogError(ex, "Falha ao enviar a foto {Blob} ao container {Container}.", nomeBlob, _container.Name);
                throw new ArmazenamentoIndisponivelException(ex);
            }

            return blob.Uri.ToString();
        }

        public void Excluir(string url)
        {
            var nomeBlob = NomeDoBlob(url, _container.Name);
            if (nomeBlob is null) return;
            try
            {
                _container.DeleteBlobIfExists(nomeBlob);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao excluir o blob órfão {Blob} do container {Container}.", nomeBlob, _container.Name);
            }
        }

        // Azure: https://<conta>.blob.core.windows.net/<container>/ssl/x.png; Azurite: http://127.0.0.1:10000/<conta>/<container>/ssl/x.png
        public static string? NomeDoBlob(string url, string container)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

            var caminho = uri.AbsolutePath.TrimStart('/');
            var prefixoContainer = container + "/";
            return caminho.StartsWith(prefixoContainer, StringComparison.OrdinalIgnoreCase)
                ? caminho[prefixoContainer.Length..]
                : $"{Subpasta}/{Path.GetFileName(uri.LocalPath)}";
        }

        private async Task GarantirContainerAsync()
        {
            if (_containerCriado) return;
            await _container.CreateIfNotExistsAsync(PublicAccessType.Blob);
            _containerCriado = true;
        }
    }
}
