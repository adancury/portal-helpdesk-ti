param(
    [Parameter(Mandatory = $true)]
    [string]$PortalUrl,

    [Parameter(Mandatory = $true)]
    [string]$ApiKey,

    [string]$Localizacao = ""
)

$ErrorActionPreference = "Stop"

function Get-PrimaryNetworkAdapter {
    $adapter = Get-NetAdapter -Physical -ErrorAction SilentlyContinue |
        Where-Object { $_.Status -eq "Up" -and $_.MacAddress } |
        Sort-Object -Property InterfaceMetric, ifIndex |
        Select-Object -First 1

    if (-not $adapter) {
        $adapter = Get-NetAdapter -ErrorAction SilentlyContinue |
            Where-Object { $_.Status -eq "Up" -and $_.MacAddress } |
            Sort-Object -Property InterfaceMetric, ifIndex |
            Select-Object -First 1
    }

    return $adapter
}

function Get-PrimaryIPv4 {
    param([string]$InterfaceAlias)

    if ([string]::IsNullOrWhiteSpace($InterfaceAlias)) {
        return $null
    }

    return Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias $InterfaceAlias -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "169.254.*" } |
        Sort-Object -Property PrefixOrigin |
        Select-Object -ExpandProperty IPAddress -First 1
}

$os = Get-CimInstance Win32_OperatingSystem
$bios = Get-CimInstance Win32_BIOS
$computer = Get-CimInstance Win32_ComputerSystem
$adapter = Get-PrimaryNetworkAdapter
$ip = if ($adapter) { Get-PrimaryIPv4 -InterfaceAlias $adapter.Name } else { $null }

$payload = [ordered]@{
    nomeComputador = $env:COMPUTERNAME
    hostname = $env:COMPUTERNAME
    sistemaOperacional = (($os.Caption, $os.Version) -join " ").Trim()
    fabricante = $computer.Manufacturer
    modelo = $computer.Model
    numeroSerie = $bios.SerialNumber
    enderecoIp = $ip
    enderecoMac = if ($adapter) { $adapter.MacAddress } else { $null }
    localizacao = $Localizacao
    usuarioLogado = $env:USERNAME
    dominio = $env:USERDOMAIN
}

$uri = $PortalUrl.TrimEnd("/") + "/api/inventario/equipamentos/coleta"
$headers = @{
    "X-Inventario-Api-Key" = $ApiKey
}

$json = $payload | ConvertTo-Json -Depth 4
$response = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $json -ContentType "application/json; charset=utf-8"

$response | ConvertTo-Json -Depth 4
