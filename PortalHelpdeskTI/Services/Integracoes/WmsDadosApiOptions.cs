namespace PortalHelpdeskTI.Services.Integracoes
{
    public class WmsDadosApiOptions
    {
        public bool Enabled { get; set; }
        public bool RunOnStartup { get; set; }
        public string BaseUrl { get; set; } = "";
        public string Token { get; set; } = "";
        public int IntervalMinutes { get; set; } = 10;
        public int LookbackDays { get; set; } = 2;
        public string CodProprietario { get; set; } = "0100";
        public int TimeoutSeconds { get; set; } = 60;
        public string[] Tipos { get; set; } = new[]
        {
            "SAIDAS",
            "ENTRADAS",
            "ESTOQUE",
            "RESSUPRIMENTOS",
            "CORTES",
            "ATIVIDADES",
            "HISTORICO_CONTAGENS",
            "ENDERECOS",
            "CURVA_ABC",
            "MOVTO_PALLETS",
            "ATIVIDADES_ENTRADA",
            "DISTRIBUICAO"
        };
    }
}
