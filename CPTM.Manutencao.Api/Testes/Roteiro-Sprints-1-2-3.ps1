# Roteiro automatizado - Sprints 1, 2 e 3 (JWT/RBAC, Triagem, Recepcao na Oficina)
# Compativel com Windows PowerShell 5.1 e PowerShell 7+.
# Uso: com a API rodando (dotnet run), execute na pasta CPTM.Manutencao.Api:
#   powershell -ExecutionPolicy Bypass -File .\Testes\Roteiro-Sprints-1-2-3.ps1
# IMPORTANTE: o repositorio mock e em memoria. REINICIE a API antes de cada execucao
# (os status mudam durante o roteiro).
param([string]$BaseUrl = "http://localhost:5203")

$script:falhas = 0

function Login($matricula) {
    $body = @{ matricula = $matricula; senha = "senha" } | ConvertTo-Json
    (Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/Auth/login" -ContentType "application/json" -Body $body).token
}

# Retorna apenas o codigo HTTP (inclusive em 4xx/5xx)
function Chamar($metodo, $caminho, $token, $corpo) {
    $headers = @{}
    if ($token) { $headers["Authorization"] = "Bearer $token" }
    $params = @{ Method = $metodo; Uri = "$BaseUrl$caminho"; Headers = $headers; UseBasicParsing = $true }
    if ($null -ne $corpo) { $params.ContentType = "application/json"; $params.Body = $corpo }
    try {
        $r = Invoke-WebRequest @params
        return [int]$r.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        throw
    }
}

function Verificar($n, $descricao, $esperado, $obtido) {
    $ok = ($esperado -eq $obtido)
    if (-not $ok) { $script:falhas++ }
    $cor = if ($ok) { "Green" } else { "Red" }
    $marca = if ($ok) { "OK   " } else { "FALHA" }
    Write-Host ("[{0}] #{1} {2} -> esperado {3}, obtido {4}" -f $marca, $n, $descricao, $esperado, $obtido) -ForegroundColor $cor
}

Write-Host "== Gerando tokens ==" -ForegroundColor Cyan
$tokComum = Login "comum123"
$tokAdmin = Login "admin123"
$tokTec   = Login "tec123"

Write-Host "== Etapa 1: Seguranca (Sprint 1) ==" -ForegroundColor Cyan
Verificar 1 "PUT /1/aprovar sem token"          401 (Chamar "Put" "/api/Solicitacoes/1/aprovar" $null)
Verificar 2 "PUT /1/aprovar como Comum"         403 (Chamar "Put" "/api/Solicitacoes/1/aprovar" $tokComum)

Write-Host "== Etapa 2: Triagem (Sprint 2) ==" -ForegroundColor Cyan
Verificar 3 "PUT /4/cancelar Comum (outro dono)" 403 (Chamar "Put" "/api/Solicitacoes/4/cancelar" $tokComum)
Verificar 4 "PUT /3/cancelar Comum (proprio)"    200 (Chamar "Put" "/api/Solicitacoes/3/cancelar" $tokComum)
Verificar 5 "PUT /2/devolver Admin (sem motivo)" 400 (Chamar "Put" "/api/Solicitacoes/2/devolver" $tokAdmin '{ "motivo": "   " }')
Verificar 6 "PUT /2/devolver Admin (com motivo)" 200 (Chamar "Put" "/api/Solicitacoes/2/devolver" $tokAdmin '{ "motivo": "Foto do equipamento ilegivel" }')
Verificar 7 "PUT /1/aprovar Admin"               200 (Chamar "Put" "/api/Solicitacoes/1/aprovar" $tokAdmin)
Verificar "7b" "PUT /3/aprovar Admin (ja cancelada)" 400 (Chamar "Put" "/api/Solicitacoes/3/aprovar" $tokAdmin)

Write-Host "== Etapa 3: Oficina (Sprint 3) ==" -ForegroundColor Cyan
Verificar "8a" "PUT /1/iniciar-manutencao Admin"   403 (Chamar "Put" "/api/Solicitacoes/1/iniciar-manutencao" $tokAdmin)
Verificar 8 "PUT /2/iniciar-manutencao Tecnico (Devolvida)" 400 (Chamar "Put" "/api/Solicitacoes/2/iniciar-manutencao" $tokTec)
Verificar 9 "PUT /1/iniciar-manutencao Tecnico (Aprovada)"  200 (Chamar "Put" "/api/Solicitacoes/1/iniciar-manutencao" $tokTec)

Write-Host ""
if ($script:falhas -eq 0) { Write-Host "TODOS OS PASSOS VALIDADOS." -ForegroundColor Green }
else { Write-Host "$($script:falhas) passo(s) com falha." -ForegroundColor Red; exit 1 }
