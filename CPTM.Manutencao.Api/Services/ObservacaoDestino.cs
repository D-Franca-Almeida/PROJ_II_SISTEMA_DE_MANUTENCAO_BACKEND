namespace CPTM.Manutencao.Api.Services
{
    // US-10: observação opcional do Admin nas ações de destino (liberar, sem reparo, inutilizar)
    public static class ObservacaoDestino
    {
        public const int TamanhoMaximo = 500;

        public static string? Normalizar(string? observacao)
        {
            if (string.IsNullOrWhiteSpace(observacao))
                return null;

            var texto = observacao.Trim();
            if (texto.Length > TamanhoMaximo)
                throw new RegraDeNegocioException($"A observação deve ter no máximo {TamanhoMaximo} caracteres.");
            return texto;
        }
    }
}
